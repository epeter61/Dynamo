﻿//Copyright 2012 Ian Keough

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Serialization;
using Dynamo.Connectors;
using Dynamo.Elements;
using Dynamo.FSchemeInterop;
using Dynamo.Utilities;
using FailureHandlingOptions = Autodesk.Revit.DB.FailureHandlingOptions;
using Transaction = Autodesk.Revit.DB.Transaction;
using TransactionStatus = Autodesk.Revit.DB.TransactionStatus;
using Path = System.IO.Path;
using Expression = Dynamo.FScheme.Expression;

namespace Dynamo.Controls
{
   /// <summary>
   /// Interaction logic for DynamoForm.xaml
   /// </summary>
   public partial class dynBench : Window, INotifyPropertyChanged
   {
      private const int CANVAS_OFFSET_Y = 55;
      private const int CANVAS_OFFSET_X = 10;

      double zoom = 1.0;
      double newX = 0.0;
      double newY = 0.0;
      double oldY = 0.0;
      double oldX = 0.0;

      dynSelection selectedElements;
      bool isConnecting = false;
      dynConnector activeConnector;
      List<DependencyObject> hitResultsList = new List<DependencyObject>();
      bool isPanning = false;
      StringWriter sw;
      string logText;

      dynWorkspace _cspace;
      dynWorkspace CurrentSpace
      {
         get { return _cspace; }
         set
         {
            _cspace = value;
            this.CurrentX = _cspace.PositionX;
            this.CurrentY = _cspace.PositionY;
            //TODO: Also set the name here.
         }
      }

      dynWorkspace homeSpace;
      public Dictionary<string, dynWorkspace> dynFunctionDict = new Dictionary<string, dynWorkspace>();

      public dynToolFinder toolFinder;
      public event PropertyChangedEventHandler PropertyChanged;

      SortedDictionary<string, TypeLoadData> builtinTypes = new SortedDictionary<string, TypeLoadData>();

      public dynBench()
      {
         this.homeSpace = this.CurrentSpace = new HomeWorkspace();

         InitializeComponent();

         sw = new StringWriter();
         Log("Welcome to Dynamo!");
         Log(String.Format("You are using build {0}.", Assembly.GetExecutingAssembly().GetName().Version.ToString()));

         dynElementSettings.SharedInstance.Workbench = workBench;
         dynElementSettings.SharedInstance.Bench = this;

         IdlePromise.RegisterIdle(dynElementSettings.SharedInstance.Doc.Application);

         //run tests, also load core library
         bool wasError = false;
         FScheme.test(
            delegate(string s)
            {
               wasError = true;
               Log(s);
            }
         );
         if (!wasError)
            Log("All Tests Passed. Core library loaded OK.");

         this.Environment = new ExecutionEnvironment();

         selectedElements = new dynSelection();

         this.CurrentX = CANVAS_OFFSET_X;
         this.CurrentY = CANVAS_OFFSET_Y;

         LoadBuiltinTypes();
         //LoadUserTypes();
      }

      private bool _activated = false;
      protected override void OnActivated(EventArgs e)
      {
         if (!this._activated)
         {
            this.LoadUserTypes();
            this.workBench.Visibility = System.Windows.Visibility.Visible;
            this._activated = true;
         }
      }

      public IEnumerable<dynElement> AllElements
      {
         get
         {
            return this.homeSpace.Elements.Concat(
               this.dynFunctionDict.Values.Aggregate(
                  (IEnumerable<dynElement>)new List<dynElement>(),
                  (a, x) => a.Concat(x.Elements)
               )
            );
         }
      }

      /// <summary>
      /// Used by various properties to notify observers that a property has changed.
      /// </summary>
      /// <param name="info">What changed.</param>
      private void NotifyPropertyChanged(String info)
      {
         if (PropertyChanged != null)
         {
            PropertyChanged(this, new PropertyChangedEventArgs(info));
         }
      }

      public string LogText
      {
         get { return logText; }
         set
         {
            logText = value;
            NotifyPropertyChanged("LogText");
         }
      }

      public double Zoom
      {
         get { return zoom; }
         set
         {
            zoom = value;
            NotifyPropertyChanged("Zoom");
         }
      }

      public double CurrentX
      {
         get { return this.CurrentSpace.PositionX; }
         set
         {
            this.CurrentSpace.PositionX = Math.Min(CANVAS_OFFSET_X, value);
            NotifyPropertyChanged("CurrentX");
         }
      }

      public double CurrentY
      {
         get { return this.CurrentSpace.PositionY; }
         set
         {
            this.CurrentSpace.PositionY = Math.Min(CANVAS_OFFSET_Y, value);
            NotifyPropertyChanged("CurrentY");
         }
      }

      //public double ZoomCenterX
      //{
      //   get { return (this.CurrentX * -1) + ((this.outerCanvas.ActualWidth / 2) / this.Zoom); }
      //}

      //public double ZoomCenterY
      //{
      //   get { return (this.CurrentY * -1) + ((this.outerCanvas.ActualHeight / 2) / this.Zoom); }
      //}

      public List<dynElement> Elements
      {
         get { return this.CurrentSpace.Elements; }
      }

      public dynSelection SelectedElements
      {
         get { return selectedElements; }
         set { selectedElements = value; }
      }

      public bool ViewingHomespace
      {
         get { return this.CurrentSpace == this.homeSpace; }
      }

      dynElement draggedElement;
      Point dragOffset;

      /// <summary>
      /// Setup the "Add" menu with all available dynElement types.
      /// </summary>
      private void LoadBuiltinTypes()
      {
         //setup the menu with all the types by reflecting
         //the DynamoElements.dll
         Assembly elementsAssembly = Assembly.GetExecutingAssembly();
         Type[] loadedTypes = elementsAssembly.GetTypes();

         foreach (Type t in loadedTypes)
         {
            //only load types that are in the right namespace, are not abstract
            //and have the elementname attribute
            object[] attribs = t.GetCustomAttributes(typeof(ElementNameAttribute), false);

            if (t.Namespace == "Dynamo.Elements" &&
                !t.IsAbstract &&
                attribs.Length > 0 &&
                t.IsSubclassOf(typeof(dynElement)))
            {
               string typeName = (attribs[0] as ElementNameAttribute).ElementName;
               builtinTypes.Add(typeName, new TypeLoadData(elementsAssembly, t));
            }
         }


         string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
         string pluginsPath = Path.Combine(directory, "definitions");

         if (Directory.Exists(pluginsPath))
         {
            loadUserAssemblies(pluginsPath);
         }

         #region PopulateUI

         var sortedExpanders = new SortedDictionary<string, Tuple<Expander, SortedList<string, dynElement>>>();

         foreach (KeyValuePair<string, TypeLoadData> kvp in builtinTypes)
         {
            //if (!kvp.Value.t.Equals(typeof(dynSymbol)))
            //{
            //   System.Windows.Controls.MenuItem mi = new System.Windows.Controls.MenuItem();
            //   mi.Header = kvp.Key;
            //   mi.Click += new RoutedEventHandler(AddElement_Click);
            //   AddMenu.Items.Add(mi);
            //}

            //---------------------//

            var catAtts = kvp.Value.t.GetCustomAttributes(typeof(ElementCategoryAttribute), false);
            string categoryName;
            if (catAtts.Length > 0)
            {
               categoryName = ((ElementCategoryAttribute)catAtts[0]).ElementCategory;
            }
            else
            {
               Log("No category specified for \"" + kvp.Key + "\"");
               continue;
            }

            dynElement newEl = null;

            try
            {
               var obj = Activator.CreateInstance(kvp.Value.t);
               //var obj = Activator.CreateInstanceFrom(kvp.Value.assembly.Location, kvp.Value.t.FullName);
               newEl = (dynElement)obj;//.Unwrap();
            }
            catch (Exception e) //TODO: Narrow down
            {
               Log("Error loading \"" + kvp.Key + "\": " + e.InnerException.Message);
               continue;
            }

            try
            {
               newEl.DisableInteraction();

               string name = kvp.Key;

               //newEl.MouseDoubleClick += delegate { AddElement(name); };

               newEl.MouseDown += delegate
               {
                  draggedElementMenuItem = newEl;
                  BeginDragElement(name, Mouse.GetPosition(newEl));
                  newEl.Visibility = System.Windows.Visibility.Hidden;
               };

               newEl.GUID = new Guid();
               newEl.Margin = new Thickness(5, 30, 5, 5);

               newEl.LayoutTransform = new ScaleTransform(.8, .8);

               Tuple<Expander, SortedList<string, dynElement>> expander;

               if (sortedExpanders.ContainsKey(categoryName))
               {
                  expander = sortedExpanders[categoryName];
               }
               else
               {
                  var e = new Expander()
                  {
                     Header = categoryName,
                     Height = double.NaN,
                     Margin = new Thickness(0, 5, 0, 0),
                     Content = new WrapPanel()
                     {
                        Height = double.NaN,
                        Width = double.NaN
                     },
                     HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                     //FontWeight = FontWeights.Bold
                  };

                  addMenuCategoryDict[categoryName] = e;

                  expander = new Tuple<Expander, SortedList<string, dynElement>>(e, new SortedList<string, dynElement>());

                  sortedExpanders[categoryName] = expander;
               }

               var sortedElements = expander.Item2;
               sortedElements.Add(kvp.Key, newEl);

               addMenuItemsDictNew[kvp.Key] = newEl;

               //--------------//

               var tagAtts = kvp.Value.t.GetCustomAttributes(typeof(ElementSearchTagsAttribute), false);
               List<string> tags = null;
               if (tagAtts.Length > 0)
               {
                  tags = ((ElementSearchTagsAttribute)tagAtts[0]).Tags;
               }

               if (tags != null)
               {
                  searchDict.Add(newEl, tags.Where(x => x.Length > 0));
               }

               searchDict.Add(newEl, kvp.Key.Split(' ').Where(x => x.Length > 0));
               searchDict.Add(newEl, kvp.Key);
            }
            catch (Exception e)
            {
               Log("Error loading \"" + kvp.Key + "\": " + e.Message);
            }
         }

         //Add everything to the menu here
         foreach (var kvp in sortedExpanders)
         {
            var expander = kvp.Value;
            this.stackPanel1.Children.Add(expander.Item1);
            var wp = (WrapPanel)expander.Item1.Content;
            foreach (dynElement e in expander.Item2.Values)
            {
               wp.Children.Add(e);
            }
         }

         #endregion
      }

      /// <summary>
      /// Setup the "Add" menu with all available user-defined types.
      /// </summary>
      public void LoadUserTypes()
      {
         string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
         string pluginsPath = Path.Combine(directory, "definitions");

         if (System.IO.Directory.Exists(pluginsPath))
         {
            loadUserWorkspaces(pluginsPath);
         }
      }

      private void loadUserWorkspaces(string directory)
      {
         string[] filePaths = Directory.GetFiles(directory, "*.dyf");
         foreach (string filePath in filePaths)
         {
            this.OpenDefinition(filePath, true);
         }
         foreach (var e in this.AllElements)
         {
            e.EnableReporting();
         }
      }

      private void loadUserAssemblies(string directory)
      {
         string[] filePaths = Directory.GetFiles(directory, "*.dll");
         foreach (string filePath in filePaths)
         {
            Assembly currAss = Assembly.LoadFrom(filePath);
            Type[] loadedTypes = currAss.GetTypes();
            foreach (Type t in loadedTypes)
            {
               //only load types that are in the right namespace, are not abstract
               //and have the elementname attribute
               object[] attribs = t.GetCustomAttributes(typeof(ElementNameAttribute), false);

               if (t.Namespace == "Dynamo.Elements" &&
                   !t.IsAbstract &&
                   attribs.Length > 0 &&
                   t.IsSubclassOf(typeof(dynElement)))
               {
                  string typeName = (attribs[0] as ElementNameAttribute).ElementName;
                  //System.Windows.Controls.MenuItem mi = new System.Windows.Controls.MenuItem();
                  //mi.Header = typeName;
                  //mi.Click += new RoutedEventHandler(AddElement_Click);
                  //AddMenu.Items.Add(mi);

                  builtinTypes.Add(typeName, new TypeLoadData(currAss, t)); //TODO: this was once usertypes
               }
            }
         }


      }

      /// <summary>
      /// This method adds dynElements when selected in the menu
      /// </summary>
      /// <param name="elementType"></param>
      /// <param name="nickName"></param>
      /// <param name="guid"></param>
      /// <param name="x"></param>
      /// <param name="y"></param>
      //public dynElement AddDynElement(Type elementType, Assembly assem, string nickName, Guid guid, double x, double y)
      //{
      //   try
      //   {
      //      //http://msdn.microsoft.com/en-us/library/ms173139.aspx
      //      //http://stackoverflow.com/questions/4993098/wpf-control-throwing-resource-identified-by-the-uri-missing-exception
      //      //http://www.matthidinger.com/archive/2008/10/12/managed-addin-framework-system.addin-with-wpf.aspx

      //      //create a new object from a type
      //      //that is passed in
      //      //dynElement el = (dynElement)Activator.CreateInstance(elementType, new object[] { nickName });
      //      var obj = Activator.CreateInstanceFrom(assem.Location, elementType.FullName);
      //      //var obj = Activator.CreateInstanceFrom(
      //      //   assem.Location,
      //      //   elementType.FullName,
      //      //   false,
      //      //   BindingFlags.CreateInstance,
      //      //   null,
      //      //   new object[] { this },
      //      //   null,
      //      //   new object[] { }
      //      //);
      //      dynElement el = (dynElement)obj.Unwrap();

      //      el.GUID = guid;

      //      //store the element in the elements list
      //      this.Elements.Add(el);

      //      //Add the element to the workbench
      //      workBench.Children.Add(el);

      //      x -= el.Width / 2;
      //      y -= el.Height / 2;

      //      while (this.Elements.Any(
      //         delegate(dynElement e)
      //         {
      //            var left = Canvas.GetLeft(e);
      //            var top = Canvas.GetTop(e);

      //            var absLeft = Math.Abs(left - x);
      //            var absTop = Math.Abs(top - y);

      //            return absLeft < 15 && absTop < 15;
      //         }))
      //      {
      //         x += 15;
      //         y += 15;
      //      }

      //      //Set its initial position based on the center of the element
      //      Canvas.SetLeft(el, x);
      //      Canvas.SetTop(el, y);

      //      //create an event on the element itself
      //      //to update the elements ports and connectors
      //      el.PreviewMouseRightButtonDown += new MouseButtonEventHandler(UpdateElement);

      //      return el;
      //   }
      //   catch (Exception e)
      //   {
      //      dynElementSettings.SharedInstance.Bench.Log(e.Message);
      //      return null;
      //   }
      //}

      /// <summary>
      /// This method adds dynElements when opening from a file
      /// </summary>
      /// <param name="elementType"></param>
      /// <param name="nickName"></param>
      /// <param name="guid"></param>
      /// <param name="x"></param>
      /// <param name="y"></param>
      /// <returns></returns>
      public dynElement AddDynElement(Type elementType, string nickName, Guid guid, double x, double y, dynWorkspace ws)
      {
         try
         {
            //create a new object from a type
            //that is passed in
            //dynElement el = (dynElement)Activator.CreateInstance(elementType, new object[] { nickName });
            dynElement el = (dynElement)Activator.CreateInstance(elementType);

            if (!string.IsNullOrEmpty(nickName))
            {
               el.NickName = nickName;
            }
            else
            {
               ElementNameAttribute elNameAttrib = el.GetType().GetCustomAttributes(typeof(ElementNameAttribute), true)[0] as ElementNameAttribute;
               if (elNameAttrib != null)
               {
                  el.NickName = elNameAttrib.ElementName;
               }
            }

            el.GUID = guid;

            string name = el.NickName;

            //store the element in the elements list
            ws.Elements.Add(el);
            el.WorkSpace = ws;

            this.workBench.Children.Add(el);

            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);

            //create an event on the element itself
            //to update the elements ports and connectors
            el.PreviewMouseRightButtonDown += new MouseButtonEventHandler(UpdateElement);

            return el;
         }
         catch (Exception)
         {
            dynElementSettings.SharedInstance.Bench.Log("Could not create an instance of the selected type.");
            return null;
         }
      }


      /// <summary>
      /// Adds the given element to the selection.
      /// </summary>
      /// <param name="sel">The element to select.</param>
      public void SelectElement(dynElement sel)
      {
         if (!selectedElements.Contains(sel))
         {
            //set all other items to the unselected state
            ClearSelection();
            selectedElements.Add(sel);
            sel.Select();
         }
      }

      /// <summary>
      /// Deselects all selected elements.
      /// </summary>
      public void ClearSelection()
      {
         //set all other items to the unselected state
         foreach (dynElement el in selectedElements)
         {
            el.Deselect();
         }
         selectedElements.Clear();
      }


      /// <summary>
      /// Called when the MouseWheel has been scrolled.
      /// </summary>
      /// <param name="sender"></param>
      /// <param name="e"></param>
      void OnMouseWheel(object sender, MouseWheelEventArgs e)
      {
         double scale = .001;
         double newValue = Convert.ToDouble(e.Delta) * scale;

         if (Zoom + newValue <= 1 && Zoom + newValue >= .001)
         {
            Zoom += newValue;
         }

         //if(this.zoomSlider.Value + newValue <= zoomSlider.Maximum &&
         //    this.zoomSlider.Value + newValue >= zoomSlider.Minimum)

         //this.zoomSlider.Value += newValue;
      }

      static bool HasParentType(Type t, Type testType)
      {
         while (t != typeof(object))
         {
            t = t.BaseType;
            if (t.Equals(testType))
               return true;
         }
         return false;
      }


      /// <summary>
      /// Updates an element and all its ports.
      /// </summary>
      /// <param name="sender"></param>
      /// <param name="e"></param>
      static void UpdateElement(object sender, MouseButtonEventArgs e)
      {
         dynElement el = sender as dynElement;
         foreach (dynPort p in el.InPorts)
         {
            p.Update();
         }
         el.OutPort.Update();
      }

      /// <summary>
      /// Find the user control of type 'testType' by traversing the tree.
      /// </summary>
      /// <returns></returns>
      public UIElement ElementClicked(DependencyObject depObj, Type testType)
      {
         UIElement foundElement = null;

         //IInputElement el = Mouse.DirectlyOver;
         //FrameworkElement fe = el as FrameworkElement;
         //DependencyObject depObj = fe.Parent;

         //walk up the tree to see whether the element is part of a port
         //then get the port's parent object
         while (depObj != null)
         {
            // If the current object is a UIElement which is a child of the
            // Canvas, exit the loop and return it.
            UIElement elem = depObj as UIElement;

            if (elem != null)
            {
               Type t = elem.GetType();

               if (HasParentType(t, testType))
               {
                  foundElement = elem;
                  return foundElement;
               }

               if (elem != null && t.Equals(testType))
               {
                  foundElement = elem;
                  return foundElement;
               }
            }

            // VisualTreeHelper works with objects of type Visual or Visual3D.
            // If the current object is not derived from Visual or Visual3D,
            // then use the LogicalTreeHelper to find the parent element.
            if (depObj is Visual)
               depObj = VisualTreeHelper.GetParent(depObj);
            else
               depObj = LogicalTreeHelper.GetParent(depObj);
         }

         return foundElement;
      }


      //Performs a hit test on the given point in the UI.
      void TestClick(System.Windows.Point pt)
      {
         // Set up a callback to receive the hit test result enumeration.
         VisualTreeHelper.HitTest(workBench, null,
             new HitTestResultCallback(MyHitTestResult),
             new PointHitTestParameters(pt));

      }

      // Return the result of the hit test to the callback.
      public HitTestResultBehavior MyHitTestResult(HitTestResult result)
      {
         // Add the hit test result to the list that will be processed after the enumeration.
         hitResultsList.Add(result.VisualHit);

         // Set the behavior to return visuals at all z-order levels.
         return HitTestResultBehavior.Continue;
      }

      void DrawGrid()
      {
         //clear the canvas's children
         workBench.Children.Clear();
         double gridSpacing = 100.0;

         for (double i = 0.0; i < workBench.Width; i += gridSpacing)
         {
            Line xLine = new Line();
            xLine.Stroke = System.Windows.Media.Brushes.LightSteelBlue;
            xLine.X1 = i;
            xLine.Y1 = 0;
            xLine.X2 = i;
            xLine.Y2 = workBench.Height;
            xLine.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            xLine.VerticalAlignment = VerticalAlignment.Center;
            xLine.StrokeThickness = 1;
            workBench.Children.Add(xLine);
            Dynamo.Controls.DragCanvas.SetCanBeDragged(xLine, false);
         }
         for (double i = 0.0; i < workBench.Height; i += gridSpacing)
         {
            Line yLine = new Line();
            yLine.Stroke = System.Windows.Media.Brushes.LightSteelBlue;
            yLine.X1 = 0;
            yLine.Y1 = i;
            yLine.X2 = workBench.Width;
            yLine.Y2 = i;
            yLine.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            yLine.VerticalAlignment = VerticalAlignment.Center;
            yLine.StrokeThickness = 1;
            workBench.Children.Add(yLine);
            Dynamo.Controls.DragCanvas.SetCanBeDragged(yLine, false);
         }
      }


      /// <summary>
      /// Called when the mouse has been moved.
      /// </summary>
      /// <param name="sender"></param>
      /// <param name="e"></param>
      public void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
      {
         //If we are currently connecting and there is an active connector,
         //redraw it to match the new mouse coordinates.
         if (isConnecting && activeConnector != null)
         {
            activeConnector.Redraw(e.GetPosition(workBench));
         }

         //If we are currently dragging an element, redraw the element to
         //match the new mouse coordinates.
         if (workBench.isDragInProgress)
         {
            dynElement el = workBench.elementBeingDragged as dynElement;
            if (el != null)
            {
               foreach (dynPort p in el.InPorts)
               {
                  p.Update();
               }
               el.OutPort.Update();
               //foreach (dynPort p in el.StatePorts)
               //{
               //   p.Update();
               //}
            }
         }

         //If we are panning the workspace, update the coordinate offset for the
         //next time we are redrawn.
         if (isPanning)
         {
            if (e.MiddleButton == MouseButtonState.Released)
            {
               isPanning = false;

               oldX = 0.0;
               oldY = 0.0;
               newX = 0.0;
               newY = 0.0;

               return;
            }

            if (oldX == 0.0)
            {
               oldX = e.GetPosition(border).X;
               oldY = e.GetPosition(border).Y;
            }
            else
            {
               newX = e.GetPosition(border).X;
               newY = e.GetPosition(border).Y;
               this.CurrentX += newX - oldX;
               this.CurrentY += newY - oldY;
               oldX = newX;
               oldY = newY;
            }
         }
      }

      private void SaveAs_Click(object sender, RoutedEventArgs e)
      {
         //string xmlPath = "C:\\test\\myWorkbench.xml";
         string xmlPath = "";

         System.Windows.Forms.SaveFileDialog saveDialog = new SaveFileDialog();
         if (saveDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
         {
            xmlPath = saveDialog.FileName;
         }

         if (!string.IsNullOrEmpty(xmlPath))
         {
            if (!SaveWorkspace(xmlPath, this.CurrentSpace))
            {
               //MessageBox.Show("Workbench could not be saved.");
               Log("Workbench could not be saved.");
            }
         }

      }


      /// <summary>
      /// Called when a mouse button is pressed.
      /// </summary>
      /// <param name="sender"></param>
      /// <param name="e"></param>
      private void OnMouseDown(object sender, MouseButtonEventArgs e)
      {
         //Pan with middle-click
         if (e.ChangedButton == MouseButton.Middle)
         {
            isPanning = true;
         }

         //close the tool finder if the user
         //has clicked anywhere else on the workbench
         if (toolFinder != null)
         {
            workBench.Children.Remove(toolFinder);
            toolFinder = null;
         }
      }


      /// <summary>
      /// Called when a mouse button is released.
      /// </summary>
      /// <param name="sender"></param>
      /// <param name="e"></param>
      private void OnMouseUp(object sender, MouseButtonEventArgs e)
      {
         //Stop panning if we have released the middle mouse button.
         if (e.ChangedButton == MouseButton.Middle)
         {
            isPanning = false;

            oldX = 0.0;
            oldY = 0.0;
            newX = 0.0;
            newY = 0.0;
         }

         if (e.ChangedButton == MouseButton.Left)
            this.beginNameEditClick = false;
      }

      //private void AddElement(string name)
      //{
      //   double x = this.CurrentX * -1 + this.outerCanvas.ActualWidth / 2;
      //   double y = this.CurrentY * -1 + this.outerCanvas.ActualHeight / 2 - this.LogScroller.ActualHeight;

      //   //TODO - Refactor
      //   //Step 1: Make sure mi.Header.ToString() is in the LoadedFunction dictionary
      //   if (this.dynFunctionDict.ContainsKey(name))
      //   {
      //      //Step 2: Make a new dynFunction
      //      //    a. Set inputs by traversing LoadedFunction entry for dynSymbols
      //      //    b. Set output to "out" for now
      //      //    c. Set symbol to mi.Header.ToString()
      //      dynWorkspace ws = this.dynFunctionDict[name];

      //      dynElement newEl = new dynFunction(
      //         ws.Elements.Where(e => e is dynSymbol)
      //            .Select(s => ((dynSymbol)s).Symbol),
      //         "out",
      //         name
      //      );

      //      newEl.GUID = Guid.NewGuid();

      //      //store the element in the elements list
      //      this.Elements.Add(newEl);

      //      //Add the element to the workbench
      //      workBench.Children.Add(newEl);

      //      x -= newEl.Width / 2;
      //      y -= newEl.Height / 2;

      //      while (this.Elements.Any(
      //         delegate(dynElement e)
      //         {
      //            var left = Canvas.GetLeft(e);
      //            var top = Canvas.GetTop(e);

      //            var absLeft = Math.Abs(left - x);
      //            var absTop = Math.Abs(top - y);

      //            return absLeft < 15 && absTop < 15;
      //         }))
      //      {
      //         x += 15;
      //         y += 15;
      //      }

      //      //Set its initial position
      //      Canvas.SetLeft(newEl, x);
      //      Canvas.SetTop(newEl, y); //this.currentY + this.workBench.ActualHeight / 2);

      //      //create an event on the element itself
      //      //to update the elements ports and connectors
      //      newEl.PreviewMouseRightButtonDown += new MouseButtonEventHandler(UpdateElement);

      //      //Step 3: CheckInputs()
      //      newEl.CheckInputs();

      //      return;
      //   }

      //   TypeLoadData tld = builtinTypes[name] as TypeLoadData;
      //   if (tld != null)
      //   {
      //      dynElement newEl = AddDynElement(tld.t, tld.assembly, name, Guid.NewGuid(), x, y);
      //      if (newEl != null)
      //      {
      //         newEl.CheckInputs();
      //         return;
      //      }
      //   }
      //}

      private void BeginDragElement(string name, Point eleOffset)
      {
         var pos = Mouse.GetPosition(overlayCanvas);

         double x = pos.X;
         double y = pos.Y;

         this.dragOffset = eleOffset;

         dynElement newEl;

         if (this.dynFunctionDict.ContainsKey(name))
         {
            dynWorkspace ws = this.dynFunctionDict[name];

            newEl = new dynFunction(
               ws.Elements.Where(e => e is dynSymbol)
                  .Select(s => ((dynSymbol)s).Symbol),
               "out",
               name
            );
         }
         else
         {
            TypeLoadData tld = builtinTypes[name];

            try
            {
               var obj = Activator.CreateInstanceFrom(tld.assembly.Location, tld.t.FullName);
               newEl = (dynElement)obj.Unwrap();
               newEl.DisableInteraction();
            }
            catch (Exception e)
            {
               dynElementSettings.SharedInstance.Bench.Log(e.Message);
               return;
            }
         }

         newEl.GUID = Guid.NewGuid();

         //Add the element to the workbench
         overlayCanvas.Children.Add(newEl);

         newEl.Opacity = 0.7;

         x -= eleOffset.X;
         y -= eleOffset.Y;

         //Set its initial position
         Canvas.SetLeft(newEl, x);
         Canvas.SetTop(newEl, y);

         this.draggedElement = newEl;

         this.overlayCanvas.IsHitTestVisible = true;
      }

      bool SaveWorkspace(string xmlPath, dynWorkspace workSpace)
      {
         Log("Saving " + xmlPath + "...");
         try
         {
            //create the xml document
            //create the xml document
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.CreateXmlDeclaration("1.0", null, null);

            XmlElement root = xmlDoc.CreateElement("dynWorkspace");  //write the root element
            root.SetAttribute("X", workSpace.PositionX.ToString());
            root.SetAttribute("Y", workSpace.PositionY.ToString());

            if (workSpace != this.homeSpace) //If we are not saving the home space
            {
               root.SetAttribute("Name", workSpace.Name);
               root.SetAttribute("Category", ((FuncWorkspace)workSpace).Category);
            }

            xmlDoc.AppendChild(root);

            XmlElement elementList = xmlDoc.CreateElement("dynElements");  //write the root element
            root.AppendChild(elementList);

            foreach (dynElement el in workSpace.Elements)
            {
               Point relPoint = el.TransformToAncestor(workBench).Transform(new Point(0, 0));

               XmlElement dynEl = xmlDoc.CreateElement(el.GetType().ToString());
               elementList.AppendChild(dynEl);

               //set the type attribute
               dynEl.SetAttribute("type", el.GetType().ToString());
               dynEl.SetAttribute("guid", el.GUID.ToString());
               dynEl.SetAttribute("nickname", el.NickName);
               dynEl.SetAttribute("x", Canvas.GetLeft(el).ToString());
               dynEl.SetAttribute("y", Canvas.GetTop(el).ToString());

               el.SaveElement(xmlDoc, dynEl);
            }

            //write only the output connectors
            XmlElement connectorList = xmlDoc.CreateElement("dynConnectors");  //write the root element
            root.AppendChild(connectorList);

            foreach (dynElement el in workSpace.Elements)
            {
               foreach (dynConnector c in el.OutPort.Connectors)
               {
                  XmlElement connector = xmlDoc.CreateElement(c.GetType().ToString());
                  connectorList.AppendChild(connector);
                  connector.SetAttribute("start", c.Start.Owner.GUID.ToString());
                  connector.SetAttribute("start_index", c.Start.Index.ToString());
                  connector.SetAttribute("end", c.End.Owner.GUID.ToString());
                  connector.SetAttribute("end_index", c.End.Index.ToString());

                  if (c.End.PortType == PortType.INPUT)
                     connector.SetAttribute("portType", "0");
               }
            }

            xmlDoc.Save(xmlPath);
         }
         catch (Exception ex)
         {
            Debug.WriteLine(ex.Message + " : " + ex.StackTrace);
            return false;
         }

         return true;
      }

      bool OpenDefinition(string xmlPath, bool reportingDisabled = false)
      {
         try
         {
            #region read xml file

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(xmlPath);

            string funName = null;
            string category = "";
            double cx = CANVAS_OFFSET_X;
            double cy = CANVAS_OFFSET_Y;

            foreach (XmlNode node in xmlDoc.GetElementsByTagName("dynWorkspace"))
            {
               foreach (XmlAttribute att in node.Attributes)
               {
                  if (att.Name.Equals("X"))
                     cx = Convert.ToDouble(att.Value);
                  else if (att.Name.Equals("Y"))
                     cy = Convert.ToDouble(att.Value);
                  else if (att.Name.Equals("Name"))
                     funName = att.Value;
                  else if (att.Name.Equals("Category"))
                     category = att.Value;
               }
            }

            //If there is no function name, then we are opening a home definition
            if (funName == null)
            {
               //View the home workspace, then open the bench file
               if (!this.ViewingHomespace)
                  this.Home_Click(null, null); //TODO: Refactor
               return this.OpenWorkbench(xmlPath);
            }

            //TODO: refactor to include x,y
            var ws = this.newFunction(
               funName,
               category.Length > 0
                  ? category
                  : BuiltinElementCategories.MISC,
               false
            );

            ws.PositionX = cx;
            ws.PositionY = cy;

            //this.Log("Opening definition " + xmlPath + "...");

            XmlNodeList elNodes = xmlDoc.GetElementsByTagName("dynElements");
            XmlNodeList cNodes = xmlDoc.GetElementsByTagName("dynConnectors");

            XmlNode elNodesList = elNodes[0] as XmlNode;
            XmlNode cNodesList = cNodes[0] as XmlNode;

            foreach (XmlNode elNode in elNodesList.ChildNodes)
            {
               XmlAttribute typeAttrib = elNode.Attributes[0];
               XmlAttribute guidAttrib = elNode.Attributes[1];
               XmlAttribute nicknameAttrib = elNode.Attributes[2];
               XmlAttribute xAttrib = elNode.Attributes[3];
               XmlAttribute yAttrib = elNode.Attributes[4];

               string typeName = typeAttrib.Value.ToString();
               Guid guid = new Guid(guidAttrib.Value.ToString());
               string nickname = nicknameAttrib.Value.ToString();

               double x = Convert.ToDouble(xAttrib.Value.ToString());
               double y = Convert.ToDouble(yAttrib.Value.ToString());

               Type t = Type.GetType(typeName);

               dynElement el = AddDynElement(t, nickname, guid, x, y, ws);
               if (reportingDisabled)
                  el.DisableReporting();

               el.LoadElement(elNode);
            }

            this.workBench.UpdateLayout();

            foreach (XmlNode connector in cNodesList.ChildNodes)
            {
               XmlAttribute guidStartAttrib = connector.Attributes[0];
               XmlAttribute intStartAttrib = connector.Attributes[1];
               XmlAttribute guidEndAttrib = connector.Attributes[2];
               XmlAttribute intEndAttrib = connector.Attributes[3];
               XmlAttribute portTypeAttrib = connector.Attributes[4];

               Guid guidStart = new Guid(guidStartAttrib.Value.ToString());
               Guid guidEnd = new Guid(guidEndAttrib.Value.ToString());
               int startIndex = Convert.ToInt16(intStartAttrib.Value.ToString());
               int endIndex = Convert.ToInt16(intEndAttrib.Value.ToString());
               int portType = Convert.ToInt16(portTypeAttrib.Value.ToString());

               //find the elements to connect
               dynElement start = null;
               dynElement end = null;

               foreach (dynElement e in ws.Elements)
               {
                  if (e.GUID == guidStart)
                  {
                     start = e;
                  }
                  else if (e.GUID == guidEnd)
                  {
                     end = e;
                  }
                  if (start != null && end != null)
                  {
                     break;
                  }
               }

               //don't connect if the end element is an instance map
               //those have a morphing set of inputs
               //dynInstanceParameterMap endTest = end as dynInstanceParameterMap;

               //if (endTest != null)
               //{
               //    continue;
               //}

               if (start != null && end != null && start != end)
               {
                  dynConnector newConnector = new dynConnector(start, end, startIndex,
                      endIndex, portType);

                  ws.Connectors.Add(newConnector);
               }
            }

            this.hideWorkspace(ws);
            this.SaveFunction(ws);
            #endregion
         }
         catch (Exception ex)
         {
            Log("There was an error opening the workbench.");
            Log(ex.Message);
            Log(ex.StackTrace);
            Debug.WriteLine(ex.Message + ":" + ex.StackTrace);
            CleanWorkbench();
            return false;
         }
         return true;
      }

      void hideWorkspace(dynWorkspace ws)
      {
         foreach (var e in ws.Elements)
            e.Visibility = System.Windows.Visibility.Collapsed;
         foreach (var c in ws.Connectors)
            c.Visible = false;
      }

      bool OpenWorkbench(string xmlPath)
      {
         Log("Opening workbench " + xmlPath + "...");
         CleanWorkbench();

         try
         {
            #region read xml file

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(xmlPath);

            foreach (XmlNode node in xmlDoc.GetElementsByTagName("dynWorkspace"))
            {
               foreach (XmlAttribute att in node.Attributes)
               {
                  if (att.Name.Equals("X"))
                     this.CurrentX = Convert.ToDouble(att.Value);
                  else if (att.Name.Equals("Y"))
                     this.CurrentY = Convert.ToDouble(att.Value);
               }
            }

            XmlNodeList elNodes = xmlDoc.GetElementsByTagName("dynElements");
            XmlNodeList cNodes = xmlDoc.GetElementsByTagName("dynConnectors");

            XmlNode elNodesList = elNodes[0] as XmlNode;
            XmlNode cNodesList = cNodes[0] as XmlNode;

            foreach (XmlNode elNode in elNodesList.ChildNodes)
            {
               XmlAttribute typeAttrib = elNode.Attributes[0];
               XmlAttribute guidAttrib = elNode.Attributes[1];
               XmlAttribute nicknameAttrib = elNode.Attributes[2];
               XmlAttribute xAttrib = elNode.Attributes[3];
               XmlAttribute yAttrib = elNode.Attributes[4];

               string typeName = typeAttrib.Value.ToString();
               Guid guid = new Guid(guidAttrib.Value.ToString());
               string nickname = nicknameAttrib.Value.ToString();

               double x = Convert.ToDouble(xAttrib.Value.ToString());
               double y = Convert.ToDouble(yAttrib.Value.ToString());

               Type t = Type.GetType(typeName);

               dynElement el = AddDynElement(
                  t, nickname, guid, x, y,
                  this.CurrentSpace
               );

               el.LoadElement(elNode);

               if (this.ViewingHomespace)
                  el.SaveResult = true;

               //read the sub elements
               //set any numeric values 
               //foreach (XmlNode subNode in elNode.ChildNodes)
               //{
               //   if (subNode.Name == "System.Double")
               //   {
               //      double val = Convert.ToDouble(subNode.Attributes[0].Value);
               //      el.OutPortData[0].Object = val;
               //      el.Update();
               //   }
               //   else if (subNode.Name == "System.Int32")
               //   {
               //      int val = Convert.ToInt32(subNode.Attributes[0].Value);
               //      el.OutPortData[0].Object = val;
               //      el.Update();
               //   }
               //}

            }

            dynElementSettings.SharedInstance.Workbench.UpdateLayout();

            foreach (XmlNode connector in cNodesList.ChildNodes)
            {
               XmlAttribute guidStartAttrib = connector.Attributes[0];
               XmlAttribute intStartAttrib = connector.Attributes[1];
               XmlAttribute guidEndAttrib = connector.Attributes[2];
               XmlAttribute intEndAttrib = connector.Attributes[3];
               XmlAttribute portTypeAttrib = connector.Attributes[4];

               Guid guidStart = new Guid(guidStartAttrib.Value.ToString());
               Guid guidEnd = new Guid(guidEndAttrib.Value.ToString());
               int startIndex = Convert.ToInt16(intStartAttrib.Value.ToString());
               int endIndex = Convert.ToInt16(intEndAttrib.Value.ToString());
               int portType = Convert.ToInt16(portTypeAttrib.Value.ToString());

               //find the elements to connect
               dynElement start = null;
               dynElement end = null;

               foreach (dynElement e in dynElementSettings.SharedInstance.Bench.Elements)
               {
                  if (e.GUID == guidStart)
                  {
                     start = e;
                  }
                  else if (e.GUID == guidEnd)
                  {
                     end = e;
                  }
                  if (start != null && end != null)
                  {
                     break;
                  }
               }

               //don't connect if the end element is an instance map
               //those have a morphing set of inputs
               //dynInstanceParameterMap endTest = end as dynInstanceParameterMap;

               //if (endTest != null)
               //{
               //    continue;
               //}

               if (start != null && end != null && start != end)
               {
                  dynConnector newConnector = new dynConnector(start, end, startIndex,
                      endIndex, portType);

                  this.CurrentSpace.Connectors.Add(newConnector);
               }
            }

            #endregion
         }
         catch (Exception ex)
         {
            Log("There was an error opening the workbench.");
            Log(ex.Message);
            Log(ex.StackTrace);
            Debug.WriteLine(ex.Message + ":" + ex.StackTrace);
            CleanWorkbench();
            return false;
         }
         return true;
      }

      private void CleanWorkbench()
      {
         Log("Clearing workflow...");

         //Copy locally
         var elements = this.Elements.ToList();

         IdlePromise.ExecuteOnIdle(
            delegate
            {
               InitTransaction();

               foreach (dynElement el in elements)
               {
                  el.DisableReporting();
                  try
                  {
                     el.Destroy();
                  }
                  catch { }
               }

               EndTransaction();
            },
            true
         );

         foreach (dynElement el in elements)
         {
            foreach (dynPort p in el.InPorts)
            {
               for (int i = p.Connectors.Count - 1; i >= 0; i--)
                  p.Connectors[i].Kill();
            }
            for (int i = el.OutPort.Connectors.Count - 1; i >= 0; i--)
               el.OutPort.Connectors[i].Kill();

            dynElementSettings.SharedInstance.Workbench.Children.Remove(el);
         }

         this.CurrentSpace.Elements.Clear();
         this.CurrentSpace.Connectors.Clear();
         this.CurrentSpace.Modified();
      }

      private void Open_Click(object sender, RoutedEventArgs e)
      {
         //string xmlPath = "C:\\test\\myWorkbench.xml";
         string xmlPath = "";

         System.Windows.Forms.OpenFileDialog openDialog = new OpenFileDialog();
         if (openDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
         {
            xmlPath = openDialog.FileName;
         }

         if (!string.IsNullOrEmpty(xmlPath))
         {
            if (!OpenDefinition(xmlPath))
            {
               //MessageBox.Show("Workbench could not be opened.");
               Log("Workbench could not be opened.");

               dynElementSettings.SharedInstance.Writer.WriteLine("Workbench could not be opened.");
               dynElementSettings.SharedInstance.Writer.WriteLine(xmlPath);
            }
         }
      }

      private void WindowClosed(object sender, EventArgs e)
      {
         if (sw != null)
         {
            sw.Close();
            dynElementSettings.SharedInstance.Writer.WriteLine("Dynamo ended " + System.DateTime.Now.ToString());
            dynElementSettings.SharedInstance.Writer.Close();
         }

         //end the transaction 
         //dynElementSettings.SharedInstance.MainTransaction.Commit();
      }

      public void Log(string message)
      {
         sw.WriteLine(message);
         LogText = sw.ToString();
         //LogScroller.ScrollToEnd();

         dynElementSettings.SharedInstance.Writer.WriteLine(message);

         LogScroller.ScrollToBottom();
      }

      void OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseEventArgs e)
      {
         //Keyboard.Focus(this);

         hitResultsList.Clear();
         TestClick(e.GetPosition(workBench));

         dynPort p = null;
         DragCanvas dc = null;
         dynElement element = null;

         bool hit = false;

         //figure out which element is hit
         //HACK: put the tests with break in highest to
         //lowest z order 
         if (hitResultsList.Count > 0)
         {
            foreach (DependencyObject depObj in hitResultsList)
            {
               //traverse the tree through all the
               //hit elements to see if you get a port
               p = ElementClicked(depObj, typeof(dynPort)) as dynPort;
               if (p != null && p.Owner.IsVisible)
               {
                  hit = true;
                  break;
               }

               //traverse the tree through all the
               //hit elements to see if you get an element
               element = ElementClicked(depObj, typeof(dynElement)) as dynElement;
               if (element != null && element.IsVisible)
               {
                  hit = true;
                  break;
               }
            }

            if (!hit)
            {
               //traverse the tree through all the
               //hit elements to see if you get the canvas
               dc = ElementClicked(hitResultsList[0], typeof(DragCanvas)) as DragCanvas;
            }
         }

         #region test for a port
         if (p != null)
         {
            Debug.WriteLine("Port clicked");

            if (!isConnecting)
            {
               //test if port already has a connection if so grab it
               //and begin connecting to somewhere else
               //don't allow the grabbing of the start connector
               if (p.Connectors.Count > 0 && p.Connectors[0].Start != p)
               {
                  activeConnector = p.Connectors[0];
                  activeConnector.Disconnect(p);
                  isConnecting = true;
                  workBench.isConnecting = true;
                  this.CurrentSpace.Connectors.Remove(activeConnector);
               }
               else
               {
                  try
                  {
                     //you've begun creating a connector
                     dynConnector c = new dynConnector(p, workBench, e.GetPosition(workBench));
                     activeConnector = c;
                     isConnecting = true;
                     workBench.isConnecting = true;
                  }
                  catch (Exception ex)
                  {
                     Debug.WriteLine(ex.Message);
                  }
               }
            }
            else
            {
               //attempt a connection between the port
               //and the connector
               if (!activeConnector.Connect(p))
               {
                  activeConnector.Kill();
                  isConnecting = false;
                  workBench.isConnecting = false;
                  activeConnector = null;
               }
               else
               {
                  //you've already started connecting
                  //now you're going to stop
                  this.CurrentSpace.Connectors.Add(activeConnector);
                  isConnecting = false;
                  workBench.isConnecting = false;
                  activeConnector = null;
               }
            }

            //set the handled flag so that the element doesn't get dragged
            e.Handled = true;
         }
         else
         {
            //if you click on the canvas and you're connecting
            //then drop the connector, otherwise do nothing
            if (activeConnector != null)
            {
               activeConnector.Kill();
               isConnecting = false;
               workBench.isConnecting = false;
               activeConnector = null;
            }

            if (editingName && !hoveringEditBox)
            {
               DisableEditNameBox();
            }

            //this.Focus();
         }
         #endregion

         if (element != null)
         {
            Debug.WriteLine("Element clicked");
            SelectElement(element);
         }

         if (dc != null)
         {
            Debug.WriteLine("Canvas clicked");
            ClearSelection();
         }
      }

      //void OnMouseRightButtonDown(object sender, System.Windows.Input.MouseEventArgs e)
      //{
      //    hitResultsList.Clear();
      //    TestClick(e.GetPosition(workBench));

      //    dynElement dynEl = null;
      //    if (hitResultsList.Count > 0)
      //    {
      //        foreach (DependencyObject depObj in hitResultsList)
      //        {
      //            //traverse the tree through all the
      //            //hit elements to see if you get a port
      //            dynEl = ElementClicked(depObj, typeof(dynElement)) as dynElement;
      //            if (dynEl != null)
      //            {
      //                break;
      //            }
      //        }
      //    }

      //    //start dragging the element
      //    if (dynEl != null)
      //    {
      //        //this.statusText.Text = "DynElement selected...";
      //        //hold off on setting the isDragInProcess
      //        workBench.isDragInProgress = true;
      //        workBench.elementBeingDragged = dynEl;
      //        workBench.DragElement();
      //    }

      //}

      //bubbling
      //from element up to root
      private void OnKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
      {


      }

      //tunneling
      //from root down to element
      private void OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
      {

      }

      private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
      {
         //handle key presses for the bench in the bubbling event
         //if no other element has already handled this event it will 
         //start at the bench and move up to root, not raising the event
         //on any other elements

         //if the key down is 'b' open the build window
         if (Keyboard.IsKeyDown(Key.LeftCtrl) && Keyboard.IsKeyDown(Key.B))
         {
            //get the mouse position

            toolFinder = new dynToolFinder();
            dynElementSettings.SharedInstance.Workbench.Children.Add(toolFinder);
            toolFinder.ToolFinderFinished += new ToolFinderFinishedHandler(toolFinder_ToolFinderFinished);

            Canvas.SetLeft(toolFinder, 100);
            Canvas.SetTop(toolFinder, 100);
            e.Handled = true;
         }
         //changed the delete key combination so as not to interfere with
         //keyboard events
         if (Keyboard.IsKeyDown(Key.LeftCtrl) && Keyboard.IsKeyDown(Key.Back) ||
             Keyboard.IsKeyDown(Key.LeftCtrl) && Keyboard.IsKeyDown(Key.Delete))
         {
            //don't do this if an input element has focus
            //this keeps us from deleting nodes when the
            //user is deleting text

            for (int i = selectedElements.Count - 1; i >= 0; i--)
            {
               DeleteElement(selectedElements[i]);
            }
            e.Handled = true;
         }

         if (editingName)
         {
            if (Keyboard.IsKeyDown(Key.Enter))
            {
               SaveNameEdit();
               DisableEditNameBox();
               e.Handled = true;
            }
            else if (Keyboard.IsKeyDown(Key.Escape))
            {
               DisableEditNameBox();
               e.Handled = true;
            }
         }
      }

      internal void DeleteElement(dynElement el)
      {
         for (int i = el.OutPort.Connectors.Count - 1; i >= 0; i--)
         {
            el.OutPort.Connectors[i].Kill();
         }
         foreach (dynPort p in el.InPorts)
         {
            for (int i = p.Connectors.Count - 1; i >= 0; i--)
            {
               p.Connectors[i].Kill();
            }
         }

         selectedElements.Remove(el);
         this.Elements.Remove(el);
         dynElementSettings.SharedInstance.Workbench.Children.Remove(el);
         el = null;
      }

      void toolFinder_ToolFinderFinished(object sender, EventArgs e)
      {
         dynElementSettings.SharedInstance.Workbench.Children.Remove(toolFinder);
         toolFinder = null;
      }

      private void New_Click(object sender, RoutedEventArgs e)
      {
         CleanWorkbench();
      }

      public ExecutionEnvironment Environment 
      {
         get;
         private set;
      }

      public bool RunInDebug { get { return this.TransMode == TransactionMode.Debug; } }

      public bool InIdleThread;

      public TransactionMode TransMode;

      private Transaction _trans;
      public void InitTransaction()
      {
         if (_trans == null || _trans.GetStatus() != TransactionStatus.Started)
         {
            _trans = new Transaction(
               dynElementSettings.SharedInstance.Doc.Document,
               "Dynamo Script"
            );
            _trans.Start();

            FailureHandlingOptions failOpt = _trans.GetFailureHandlingOptions();
            failOpt.SetFailuresPreprocessor(new DynamoWarningPrinter(this));
            _trans.SetFailureHandlingOptions(failOpt);
         }
      }

      public Transaction Transaction { get { return this._trans; } }

      public void EndTransaction()
      {
         if (_trans != null)
         {
            if (_trans.GetStatus() == TransactionStatus.Started)
               _trans.Commit();
            _trans = null;
         }
      }

      public void CancelTransaction()
      {
         if (_trans != null)
         {
            _trans.RollBack();
            _trans = null;
         }
      }

      public bool IsTransactionActive()
      {
         return _trans != null;
      }

      public bool Running = false;

      public void RunExpression(bool debug, bool showErrors = true)
      {
         if (this.Running)
            return;

         this.Running = true;

         this.dynamicRun = !showErrors;

         BackgroundWorker worker = new BackgroundWorker();

         worker.DoWork += delegate(object s, DoWorkEventArgs args)
         {
            var topElements = this.homeSpace.Elements.Where(x => !x.OutPort.Connectors.Any());

            //Mark the topmost as dirty/clean
            foreach (var topMost in topElements)
               topMost.MarkDirty();

            //TODO: Flesh out error handling
            try
            {
               Action run = delegate
               {
                  foreach (dynElement topMost in topElements)
                  {
                     Expression runningExpression = topMost.Build().Compile();

                     string exp = FScheme.print(runningExpression);

                     this.Dispatcher.Invoke(new Action(
                        () => Log("> " + exp)
                     ));

                     try
                     {
                        var expr = this.Environment.Evaluate(runningExpression);

                        this.Dispatcher.Invoke(new Action(
                           () => Log(FScheme.print(expr))
                        ));
                     }
                     catch (Exception ex)
                     {
                        if (ex.Message.Length > 0)
                        {
                           this.Dispatcher.Invoke(new Action(
                              () => Log("ERROR: " + ex.Message)
                           ));
                        }

                        if (_trans != null)
                        {
                           _trans.RollBack();
                           _trans = null;
                        }

                        this.CancelRun = false;
                        this.runAgain = false;
                     }
                  }

                  Action delete = delegate
                  {
                     this.InitTransaction();

                     foreach (var element in this.AllElements)
                     {
                        element.ResetRuns();
                     }

                     this.EndTransaction();
                  };

                  if (debug || !this.InIdleThread)
                     IdlePromise.ExecuteOnIdle(delete, false);
                  else
                     delete();
               };

               if (!debug)
               {
                  Func<dynElement, bool> allIdlePred = delegate(dynElement e)
                  {
                     object[] attribs = e.GetType().GetCustomAttributes(typeof(RequiresTransactionAttribute), false);
                     if (attribs.Length > 0)
                     {
                        return !(attribs[0] as RequiresTransactionAttribute).RequiresTransaction;
                     }

                     return true;
                  };

                  bool allInIdleThread = this.AllElements.Any(x => x is dynTransaction)
                                         || this.AllElements.All(allIdlePred);

                  if (allInIdleThread)
                  {
                     this.TransMode = TransactionMode.Manual;
                     this.InIdleThread = false;
                     run();
                  }
                  else
                  {
                     this.TransMode = TransactionMode.Automatic;
                     this.InIdleThread = true;

                     IdlePromise.ExecuteOnIdle(run, false);
                  }
               }
               else
               {
                  this.TransMode = TransactionMode.Debug;
                  this.InIdleThread = true;

                  this.Dispatcher.Invoke(new Action(
                     () => Log("Running expression in debug.")
                  ));

                  run();
               }
            }
            catch (Exception ex)
            {
               if (ex.Message.Length > 0)
               {
                  this.Dispatcher.Invoke(new Action(
                     () => Log("ERROR: " + ex.Message)
                  ));
               }

               _trans = null;
               this.runAgain = false;
            }
            finally
            {
               this.runButton.Dispatcher.Invoke(new Action(
                  delegate { this.runButton.IsEnabled = true; }
               ));
               this.Running = false;
               if (this.runAgain)
               {
                  this.runAgain = false;
                  this.Dispatcher.BeginInvoke(new Action(
                     delegate { this.RunExpression(debug, showErrors); }
                  ));
               }
            }
         };

         this.runButton.Dispatcher.Invoke(new Action(
            delegate { this.runButton.IsEnabled = false; }
         ));

         worker.RunWorkerAsync();
      }

      private void Run_Click(object sender, RoutedEventArgs e)
      {
         RunExpression(this.debugCheckBox.IsChecked == true);
      }

      private void RunDebug_Click(object sender, RoutedEventArgs e)
      {
         RunExpression(true);
      }

      private void SaveFunction_Click(object sender, RoutedEventArgs e)
      {
         SaveFunction(this.CurrentSpace);
      }

      //private Dictionary<string, System.Windows.Controls.MenuItem> addMenuItemsDict
      //   = new Dictionary<string, System.Windows.Controls.MenuItem>();

      private Dictionary<string, System.Windows.Controls.MenuItem> viewMenuItemsDict
         = new Dictionary<string, System.Windows.Controls.MenuItem>();

      private Dictionary<string, Expander> addMenuCategoryDict
         = new Dictionary<string, Expander>();

      private Dictionary<string, dynElement> addMenuItemsDictNew
         = new Dictionary<string, dynElement>();

      private void NewFunction_Click(object sender, RoutedEventArgs e)
      {
         //First, prompt the user to enter a name
         string name, category;
         string error = "";

         do
         {
            var dialog = new FunctionNamePrompt(this.addMenuCategoryDict.Keys, error);
            if (dialog.ShowDialog() != true)
            {
               return;
            }

            name = dialog.Text;
            category = dialog.Category;

            if (this.dynFunctionDict.ContainsKey(name))
            {
               error = "A function with this name already exists.";
            }
            else if (category.Equals(""))
            {
               error = "Please enter a valid category.";
            }
            else
            {
               error = "";
            }
         }
         while (!error.Equals(""));

         this.newFunction(name, category, true);
      }

      private dynWorkspace newFunction(string name, string category, bool display)
      {
         //Add an entry to the funcdict
         var workSpace = new FuncWorkspace(name, category, CANVAS_OFFSET_X, CANVAS_OFFSET_Y);

         var newElements = workSpace.Elements;
         var newConnectors = workSpace.Connectors;

         this.dynFunctionDict[name] = workSpace;

         //Add an entry to the View menu
         System.Windows.Controls.MenuItem i = new System.Windows.Controls.MenuItem();
         i.Header = name;
         i.Click += new RoutedEventHandler(ChangeView_Click);
         this.viewMenu.Items.Add(i);
         this.viewMenuItemsDict[name] = i;

         //Add an entry to the Add menu
         //System.Windows.Controls.MenuItem mi = new System.Windows.Controls.MenuItem();
         //mi.Header = name;
         //mi.Click += new RoutedEventHandler(AddElement_Click);
         //AddMenu.Items.Add(mi);
         //this.addMenuItemsDict[name] = mi;

         dynFunction newEl = new dynFunction(
            workSpace.Elements.Where(el => el is dynSymbol)
               .Select(s => ((dynSymbol)s).Symbol),
            "out",
            name
         );
         newEl.DisableInteraction();
         newEl.MouseDown += delegate
         {
            draggedElementMenuItem = newEl;

            BeginDragElement(newEl.Symbol, Mouse.GetPosition(newEl));

            newEl.Visibility = System.Windows.Visibility.Hidden;
         };
         newEl.GUID = Guid.NewGuid();
         newEl.Margin = new Thickness(5, 30, 5, 5);
         newEl.LayoutTransform = new ScaleTransform(.8, .8);

         Expander expander;

         if (addMenuCategoryDict.ContainsKey(category))
         {
            expander = addMenuCategoryDict[category];
         }
         else
         {
            expander = new Expander()
            {
               Header = category,
               Height = double.NaN,
               Margin = new Thickness(0, 5, 0, 0),
               Content = new WrapPanel()
               {
                  Height = double.NaN,
                  Width = 240
               },
               HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
               //FontWeight = FontWeights.Bold
            };

            addMenuCategoryDict[category] = expander;

            var sortedExpanders = new SortedList<string, Expander>();
            foreach (Expander child in this.stackPanel1.Children)
            {
               sortedExpanders.Add((string)child.Header, child);
            }
            sortedExpanders.Add(category, expander);

            this.stackPanel1.Children.Clear();

            foreach (Expander child in sortedExpanders.Values)
            {
               this.stackPanel1.Children.Add(child);
            }
         }

         var wp = (WrapPanel)expander.Content;

         var sortedElements = new SortedList<string, dynElement>();
         foreach (dynElement child in wp.Children)
         {
            sortedElements.Add(child.NickName, child);
         }
         sortedElements.Add(name, newEl);

         wp.Children.Clear();

         foreach (dynElement child in sortedElements.Values)
         {
            wp.Children.Add(child);
         }

         addMenuItemsDictNew[name] = newEl;
         searchDict.Add(newEl, name.Split(' ').Where(x => x.Length > 0));

         if (display)
         {
            //Store old workspace
            //var ws = new dynWorkspace(this.elements, this.connectors, this.CurrentX, this.CurrentY);

            if (!this.ViewingHomespace)
            {
               //Step 2: Store function workspace in the function dictionary
               this.dynFunctionDict[this.CurrentSpace.Name] = this.CurrentSpace;

               //Step 3: Save function
               this.SaveFunction(this.CurrentSpace);
            }

            //Make old workspace invisible
            foreach (dynElement dynE in this.Elements)
            {
               dynE.Visibility = System.Windows.Visibility.Collapsed;
            }
            foreach (dynConnector dynC in this.CurrentSpace.Connectors)
            {
               dynC.Visible = false;
            }

            //this.currentFunctionName = name;

            ////Clear the bench for the new function
            //this.elements = newElements;
            //this.connectors = newConnectors;
            //this.CurrentX = CANVAS_OFFSET_X;
            //this.CurrentY = CANVAS_OFFSET_Y;
            this.CurrentSpace = workSpace;

            this.saveFuncItem.IsEnabled = true;
            this.homeButton.IsEnabled = true;
            //this.varItem.IsEnabled = true;

            this.workspaceLabel.Content = this.CurrentSpace.Name;
            this.editNameButton.Visibility = System.Windows.Visibility.Visible;
            this.editNameButton.IsHitTestVisible = true;
            this.setFunctionBackground();
         }

         return workSpace;
      }

      private void ChangeView_Click(object sender, RoutedEventArgs e)
      {
         System.Windows.Controls.MenuItem item = sender as System.Windows.Controls.MenuItem;

         DisplayFunction(item.Header.ToString());
      }

      private void Home_Click(object sender, RoutedEventArgs e)
      {
         //Step 1: Make function workspace invisible
         foreach (var ele in this.Elements)
         {
            ele.Visibility = System.Windows.Visibility.Collapsed;
         }
         foreach (var con in this.CurrentSpace.Connectors)
         {
            con.Visible = false;
         }

         //var ws = new dynWorkspace(this.elements, this.connectors, this.CurrentX, this.CurrentY);

         //Step 2: Store function workspace in the function dictionary
         this.dynFunctionDict[this.CurrentSpace.Name] = this.CurrentSpace;

         //Step 3: Save function
         this.SaveFunction(this.CurrentSpace);

         //Step 4: Make home workspace visible
         //this.elements = this.homeSpace.elements;
         //this.connectors = this.homeSpace.connectors;
         //this.CurrentX = this.homeSpace.savedX;
         //this.CurrentY = this.homeSpace.savedY;
         this.CurrentSpace = this.homeSpace;

         foreach (var ele in this.Elements)
         {
            ele.Visibility = System.Windows.Visibility.Visible;
         }
         foreach (var con in this.CurrentSpace.Connectors)
         {
            con.Visible = true;
         }

         this.saveFuncItem.IsEnabled = false;
         this.homeButton.IsEnabled = false;
         //this.varItem.IsEnabled = false;

         this.workspaceLabel.Content = "Home";
         this.editNameButton.Visibility = System.Windows.Visibility.Collapsed;
         this.editNameButton.IsHitTestVisible = false;

         this.setHomeBackground();
      }

      public void SaveFunction(dynWorkspace funcWorkspace, bool writeDefinition = true)
      {
         //Step 1: Generate xml, and save it in a fixed place
         if (writeDefinition)
         {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string pluginsPath = Path.Combine(directory, "definitions");

            try
            {
               if (!Directory.Exists(pluginsPath))
                  Directory.CreateDirectory(pluginsPath);

               string path = Path.Combine(pluginsPath, FormatFileName(funcWorkspace.Name) + ".dyf");
               SaveWorkspace(path, funcWorkspace);
            }
            catch (Exception e)
            {
               Log("Error saving:" + e.GetType() + ": " + e.Message);
            }
         }

         //Step 2: Find function entry point, and then compile the function and add it to our environment
         dynElement topMost = funcWorkspace.Elements.FirstOrDefault(
            el => !el.OutPort.Connectors.Any()
         );

         var variables = funcWorkspace.Elements.Where(x => x is dynSymbol);
         var variableNames = variables.Select(x => ((dynSymbol)x).Symbol);

         try
         {
            if (topMost != default(dynElement))
            {
               Expression expression = Utils.MakeAnon(
                  variableNames,
                  topMost.Build().Compile()
               );

               this.Environment.DefineSymbol(funcWorkspace.Name, expression);
            }
         }
         catch
         {
            //TODO: flesh out error handling (build-loops?)
         }

         //Step 3: Update existing function nodes which point to this function to match its changes
         foreach (var el in this.AllElements)
         {
            if (el is dynFunction)
            {
               var node = (dynFunction)el;

               if (!node.Symbol.Equals(funcWorkspace.Name))
                  continue;

               node.SetInputs(variableNames);
               el.ReregisterInputs();
               //el.IsDirty = true;
            }
         }

         //Step 4: Call OnSave for all saved elements
         foreach (var el in funcWorkspace.Elements)
            el.OnSave();

         //Step 5: Update new add menu
         var addItem = (dynFunction)this.addMenuItemsDictNew[funcWorkspace.Name];
         addItem.SetInputs(variableNames);
         addItem.ReregisterInputs();
      }

      internal void DisplayFunction(string symbol)
      {
         if (!this.dynFunctionDict.ContainsKey(symbol))
            return;

         var newWs = this.dynFunctionDict[symbol];

         //Make sure we aren't dragging
         workBench.isDragInProgress = false;
         workBench.ignoreClick = true;

         //Step 1: Make function workspace invisible
         foreach (var ele in this.Elements)
         {
            ele.Visibility = System.Windows.Visibility.Collapsed;
         }
         foreach (var con in this.CurrentSpace.Connectors)
         {
            con.Visible = false;
         }

         //var ws = new dynWorkspace(this.elements, this.connectors, this.CurrentX, this.CurrentY);

         if (!this.ViewingHomespace)
         {
            //Step 2: Store function workspace in the function dictionary
            this.dynFunctionDict[this.CurrentSpace.Name] = this.CurrentSpace;

            //Step 3: Save function
            this.SaveFunction(this.CurrentSpace);
         }

         //Step 4: Make home workspace visible
         //this.elements = newWs.elements;
         //this.connectors = newWs.connectors;
         //this.CurrentX = newWs.savedX;
         //this.CurrentY = newWs.savedY;
         this.CurrentSpace = newWs;

         foreach (var ele in this.Elements)
         {
            ele.Visibility = System.Windows.Visibility.Visible;
         }
         foreach (var con in this.CurrentSpace.Connectors)
         {
            con.Visible = true;
         }


         this.saveFuncItem.IsEnabled = true;
         this.homeButton.IsEnabled = true;
         //this.varItem.IsEnabled = true;

         this.workspaceLabel.Content = symbol;
         this.editNameButton.Visibility = System.Windows.Visibility.Visible;
         this.editNameButton.IsHitTestVisible = true;

         this.setFunctionBackground();
      }


      private void setFunctionBackground()
      {
         var bgBrush = (LinearGradientBrush)this.outerCanvas.Background;
         bgBrush.GradientStops[0].Color = Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B); //Dark
         bgBrush.GradientStops[1].Color = Color.FromArgb(0xFF, 0xBA, 0xBA, 0xBA); //Light

         //var sbBrush = (LinearGradientBrush)this.sidebarGrid.Background;
         //sbBrush.GradientStops[0].Color = Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B); //Dark
         //sbBrush.GradientStops[1].Color = Color.FromArgb(0xFF, 0xBA, 0xBA, 0xBA); //Light
      }


      private void setHomeBackground()
      {
         var bgBrush = (LinearGradientBrush)this.outerCanvas.Background;
         bgBrush.GradientStops[0].Color = Color.FromArgb(0xFF, 0x4B, 0x4B, 0x4B); //Dark
         bgBrush.GradientStops[1].Color = Color.FromArgb(0xFF, 0x7A, 0x7A, 0x7A); //Light

         //var sbBrush = (LinearGradientBrush)this.sidebarGrid.Background;
         //sbBrush.GradientStops[0].Color = Color.FromArgb(0xFF, 0x4B, 0x4B, 0x4B); //Dark
         //sbBrush.GradientStops[1].Color = Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A); //Light
      }


      private void Print_Click(object sender, RoutedEventArgs e)
      {
         foreach (dynElement el in this.Elements)
         {
            dynElement topMost = null;
            if (!el.OutPort.Connectors.Any())
            {
               topMost = el;

               Expression runningExpression = topMost.Build().Compile();

               //TODO: Flesh out error handling
               try
               {
                  string exp = FScheme.print(runningExpression);
                  Log("> " + exp);
               }
               catch (Exception ex)
               {
                  Log("ERROR: " + ex.Message);
               }
            }
         }
      }

      internal void RemoveConnector(dynConnector c)
      {
         this.CurrentSpace.Connectors.Remove(c);
      }

      internal void ShowElement(dynElement e)
      {
         if (dynamicRun)
            return;

         if (!this.Elements.Contains(e))
         {
            if (this.homeSpace != null && this.homeSpace.Elements.Contains(e))
            {
               //Show the homespace
               Home_Click(null, null);
            }
            else
            {
               foreach (var funcPair in this.dynFunctionDict)
               {
                  if (funcPair.Value.Elements.Contains(e))
                  {
                     DisplayFunction(funcPair.Key);
                     break;
                  }
               }
            }
         }

         CenterViewOnElement(e);
      }

      private void CenterViewOnElement(dynElement e)
      {
         var left = Canvas.GetLeft(e);
         var top = Canvas.GetTop(e);

         var x = left + e.Width / 2 - this.outerCanvas.ActualWidth / 2;
         var y = top + e.Height / 2 - (this.outerCanvas.ActualHeight / 2 - this.LogScroller.ActualHeight);

         this.CurrentX = -x;
         this.CurrentY = -y;
      }

      private bool beginNameEditClick;
      private bool editingName;

      private void image1_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
      {
         //highlight

         if (beginNameEditClick && e.LeftButton == MouseButtonState.Released)
         {
            beginNameEditClick = false;
         }
      }

      private void image1_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
      {
         //unhighlight
      }

      private void image1_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
      {
         if (beginNameEditClick)
         {
            if (editingName)
            {
               SaveNameEdit();
               DisableEditNameBox();
            }
            else
            {
               EnableEditNameBox();
            }
         }
         beginNameEditClick = false;
      }

      private void image1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
      {
         beginNameEditClick = true;
      }

      void EnableEditNameBox()
      {
         this.workspaceLabel.Visibility = System.Windows.Visibility.Collapsed;

         this.editNameBox.Visibility = System.Windows.Visibility.Visible;
         this.editNameBox.IsEnabled = true;
         this.editNameBox.IsHitTestVisible = true;
         this.editNameBox.Focusable = true;
         this.editNameBox.Focus();
         this.editNameBox.Text = this.CurrentSpace.Name;
         this.editNameBox.SelectAll();

         editingName = true;
      }

      void SaveNameEdit()
      {
         var newName = this.editNameBox.Text;

         this.workspaceLabel.Content = this.editNameBox.Text;

         //Update view menu
         var viewItem = this.viewMenuItemsDict[this.CurrentSpace.Name];
         viewItem.Header = newName;
         this.viewMenuItemsDict.Remove(this.CurrentSpace.Name);
         this.viewMenuItemsDict[newName] = viewItem;

         //Update add menu
         //var addItem = this.addMenuItemsDict[this.currentFunctionName];
         //addItem.Header = newName;
         //this.addMenuItemsDict.Remove(this.currentFunctionName);
         //this.addMenuItemsDict[newName] = addItem;

         //------------------//

         var newAddItem = (dynFunction)this.addMenuItemsDictNew[this.CurrentSpace.Name];
         if (newAddItem.NickName.Equals(this.CurrentSpace.Name))
            newAddItem.NickName = newName;
         newAddItem.Symbol = newName;
         this.addMenuItemsDictNew.Remove(this.CurrentSpace.Name);
         this.addMenuItemsDictNew[newName] = newAddItem;

         //Sort the menu after a rename
         Expander unsorted = this.addMenuCategoryDict.Values.FirstOrDefault(
            ex => ((WrapPanel)ex.Content).Children.Contains(newAddItem)
         );

         var wp = (WrapPanel)unsorted.Content;

         var sortedElements = new SortedList<string, dynElement>();
         foreach (dynElement child in wp.Children)
         {
            sortedElements.Add(child.NickName, child);
         }

         wp.Children.Clear();

         foreach (dynElement child in sortedElements.Values)
         {
            wp.Children.Add(child);
         }

         //Update search dictionary after a rename
         var oldTags = this.CurrentSpace.Name.Split(' ').Where(x => x.Length > 0);
         this.searchDict.Remove(newAddItem, oldTags);

         var newTags = newName.Split(' ').Where(x => x.Length > 0);
         this.searchDict.Add(newAddItem, newTags);

         //------------------//

         //Update existing function nodes
         foreach (var el in this.AllElements)
         {
            if (el is dynFunction)
            {
               var node = (dynFunction)el;

               if (!node.Symbol.Equals(this.CurrentSpace.Name))
                  continue;

               node.Symbol = newName;

               //Rename nickname only if it's still referring to the old name
               if (node.NickName.Equals(this.CurrentSpace.Name))
                  node.NickName = newName;
            }
         }

         this.Environment.RemoveSymbol(this.CurrentSpace.Name);

         //TODO: Delete old stored definition
         string directory = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
         string pluginsPath = System.IO.Path.Combine(directory, "definitions");

         if (Directory.Exists(pluginsPath))
         {
            string oldpath = System.IO.Path.Combine(pluginsPath, this.CurrentSpace.Name + ".dyf");
            string newpath = FormatFileName(
               System.IO.Path.Combine(pluginsPath, newName + ".dyf")               
            );

            File.Move(oldpath, newpath);
         }

         //Update function dictionary
         var tmp = this.dynFunctionDict[this.CurrentSpace.Name];
         this.dynFunctionDict.Remove(this.CurrentSpace.Name);
         this.dynFunctionDict[newName] = tmp;

         ((FuncWorkspace)this.CurrentSpace).Name = newName;

         this.SaveFunction(this.CurrentSpace);
      }

      private static string FormatFileName(string filename)
      {
         return RemoveChars(
            filename, 
            new string[] { "\\", "/", ":", "*", "?", "\"", "<", ">", "|" }
         );
      }

      private static string RemoveChars(string s, IEnumerable<string> chars)
      {
         foreach (var c in chars)
            s = s.Replace(c, "");
         return s;
      }

      void DisableEditNameBox()
      {
         this.editNameBox.Visibility = System.Windows.Visibility.Collapsed;
         this.editNameBox.IsEnabled = false;
         this.editNameBox.IsHitTestVisible = false;
         this.editNameBox.Focusable = false;

         this.workspaceLabel.Visibility = System.Windows.Visibility.Visible;

         editingName = false;
      }

      private bool hoveringEditBox = false;
      private dynElement draggedElementMenuItem;

      private void editNameBox_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
      {
         hoveringEditBox = true;
      }

      private void editNameBox_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
      {
         hoveringEditBox = false;
      }

      private void OverlayCanvas_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
      {
         var el = draggedElement;

         var pos = e.GetPosition(overlayCanvas);

         Canvas.SetLeft(el, pos.X - dragOffset.X);
         Canvas.SetTop(el, pos.Y - dragOffset.Y);
      }

      private void OverlayCanvas_OnMouseUp(object sender, MouseButtonEventArgs e)
      {
         var el = draggedElement;

         var pos = e.GetPosition(this.workBench);

         this.overlayCanvas.Children.Clear();
         this.overlayCanvas.IsHitTestVisible = false;

         draggedElementMenuItem.Visibility = System.Windows.Visibility.Visible;
         draggedElementMenuItem = null;

         var outerPos = e.GetPosition(this.outerCanvas);

         if (outerPos.X >= 0 && outerPos.X <= this.overlayCanvas.ActualWidth
             && outerPos.Y >= 0 && outerPos.Y <= this.overlayCanvas.ActualHeight)
         {
            this.workBench.Children.Add(el);

            this.Elements.Add(el);

            el.WorkSpace = this.CurrentSpace;

            el.Opacity = 1;

            Canvas.SetLeft(el, Math.Max(pos.X - dragOffset.X, 0));
            Canvas.SetTop(el, Math.Max(pos.Y - dragOffset.Y, 0));

            el.EnableInteraction();

            if (this.ViewingHomespace)
               el.SaveResult = true;
         }

         dragOffset = new Point();
      }

      SearchDictionary<dynElement> searchDict = new SearchDictionary<dynElement>();

      private bool dynamicRun = false;
      private bool runAgain = false;

      void FilterAddMenu(HashSet<dynElement> elements)
      {
         foreach (Expander ex in this.stackPanel1.Children)
         {
            this.filterCategory(elements, ex);
         }
      }

      private void filterCategory(HashSet<dynElement> elements, Expander ex)
      {
         var content = (WrapPanel)ex.Content;

         bool filterWholeCategory = true;

         foreach (dynElement ele in content.Children)
         {
            if (!elements.Contains(ele))
            {
               ele.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
               ele.Visibility = System.Windows.Visibility.Visible;
               filterWholeCategory = false;
            }
         }

         if (filterWholeCategory)
         {
            ex.Visibility = System.Windows.Visibility.Collapsed;
         }
         else
         {
            ex.Visibility = System.Windows.Visibility.Visible;

            //if (filter.Length > 0)
            //   ex.IsExpanded = true;
         }
      }

      private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
      {
         var search = this.searchBox.Text.Trim();

         var filter = search.Length == 0
            ? new HashSet<dynElement>(this.addMenuItemsDictNew.Values)
            : searchDict.Search(search.ToLower());

         this.FilterAddMenu(filter);
      }

      private void searchBox_LostFocus(object sender, RoutedEventArgs e)
      {
         //if (this.searchBox.Text.Equals(""))
         //   this.searchBox.Text = "Search";
      }

      public bool CancelRun
      {
         get;
         private set;
      }

      private void Cancel_Click(object sender, RoutedEventArgs e)
      {
         this.CancelRun = true;
      }

      public bool DynamicRunEnabled
      {
         get { return this.dynamicCheckBox.IsChecked == true; }
      }

      internal void QueueRun()
      {
         this.runAgain = true;
      }
   }

   public class dynSelection : ObservableCollection<dynElement>
   {
      public dynSelection() : base() { }
   }

   public class TypeLoadData
   {
      public Assembly assembly;
      public Type t;

      public TypeLoadData(Assembly assemblyIn, Type typeIn)
      {
         assembly = assemblyIn;
         t = typeIn;
      }
   }

   public enum TransactionMode
   {
      Automatic,
      Manual,
      Debug
   }

   public class DynamoWarningPrinter : Autodesk.Revit.DB.IFailuresPreprocessor
   {
      dynBench bench;

      public DynamoWarningPrinter(dynBench b)
      {
         this.bench = b;
      }

      public Autodesk.Revit.DB.FailureProcessingResult PreprocessFailures(Autodesk.Revit.DB.FailuresAccessor failuresAccessor)
      {
         var failList = failuresAccessor.GetFailureMessages();
         foreach (var fail in failList)
         {
            bench.Log(
               "Warning: " + fail.GetDescriptionText()
            );
            failuresAccessor.DeleteWarning(fail);
         }

         return Autodesk.Revit.DB.FailureProcessingResult.Continue;
      }
   }

}