﻿//Copyright © Autodesk, Inc. 2012. All rights reserved.
//
//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at
//
//http://www.apache.org/licenses/LICENSE-2.0
//
//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Linq;
using System.Xml;
using Autodesk.Revit.DB;
using Dynamo.Utilities;
using Dynamo.Connectors;
using Dynamo.Revit;
using Dynamo.Revit.SyncedNodeExtensions; //Gives the RegisterEval... methods
using Microsoft.FSharp.Collections;
using Value = Dynamo.FScheme.Value;
using Dynamo.FSchemeInterop;

namespace Dynamo.Nodes
{
    [IsInteractive(true)]
    public abstract class dynElementSelectionBase : dynNodeWithOneOutput
    {
        private TextBox _tb;
        private Button _selectButton;

        protected string _selectButtonContent;

        public string SelectButtonContent
        {
            get { return _selectButtonContent; }
            set
            {
                _selectButtonContent = value;
                RaisePropertyChanged("SelectButtonContent");
            }
        }

        /// <summary>
        /// Determines what the text should read on the node when the selection has been changed.
        /// Is ignored in the case where nothing is selected.
        /// </summary>
        //protected abstract string SelectionText { get; }
        protected string _selectionText;

        public abstract string SelectionText { get; set; }

        protected dynElementSelectionBase(PortData outPortData)
        {
            OutPortData.Add(outPortData);
            RegisterAllPorts();
        }

        public override void SetupCustomUIElements(Controls.dynNodeView nodeUI)
        {
            //add a button to the inputGrid on the dynElement
            _selectButton = new dynNodeButton
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            _selectButton.Click += selectButton_Click;

            _tb = new TextBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false
            };

            //tb.Text = "Nothing Selected";
            if (SelectedElement == null || !SelectionText.Any() || !SelectButtonContent.Any())
            {
                SelectionText = "Nothing Selected";
                SelectButtonContent = "Select Instance";
            }

            //NodeUI.SetRowAmount(2);
            nodeUI.inputGrid.RowDefinitions.Add(new RowDefinition());
            nodeUI.inputGrid.RowDefinitions.Add(new RowDefinition());

            nodeUI.inputGrid.Children.Add(_tb);
            nodeUI.inputGrid.Children.Add(_selectButton);

            System.Windows.Controls.Grid.SetRow(_selectButton, 0);
            System.Windows.Controls.Grid.SetRow(_tb, 1);

            _tb.DataContext = this;
            _selectButton.DataContext = this;

            var selectTextBinding = new System.Windows.Data.Binding("SelectionText")
            {
                Mode = BindingMode.TwoWay,
            };
            _tb.SetBinding(TextBox.TextProperty, selectTextBinding);

            var buttonTextBinding = new System.Windows.Data.Binding("SelectButtonContent")
            {
                Mode = BindingMode.TwoWay,
            };
            _selectButton.SetBinding(ContentControl.ContentProperty, buttonTextBinding);
        }

        private void selectButton_Click(object sender, RoutedEventArgs e)
        {
            _selectButton.IsEnabled = false;
            IdlePromise.ExecuteOnIdle(
                delegate
                {
                    OnSelectClick();
                    _selectButton.IsEnabled = true;
                });
        }

        /// <summary>
        /// Callback for when the "Select" button has been clicked.
        /// </summary>
        /// 
        /// 
        protected abstract void OnSelectClick();

        private Element _selected;

        /// <summary>
        /// The Element which is selected. Setting this property will automatically register the Element
        /// for proper updating, and will update this node's IsDirty value.
        /// </summary>
        public virtual Element SelectedElement
        {
            get { return _selected; }
            set
            {
                bool dirty;
                if (_selected != null)
                {
                    if (value != null && value.Id.Equals(_selected.Id))
                        return;

                    dirty = true;
                    this.UnregisterEvalOnModified(_selected.Id);
                }
                else
                    dirty = value != null;

                _selected = value;
                if (value != null)
                {
                    this.RegisterEvalOnModified(
                        value.Id,
                        delAction: delegate
                        {
                            _selected = null;
                            SelectedElement = null;
                        }
                        );

                    //this.tb.Text = this.SelectionText;
                    //this.selectButton.Content = "Change";
                    SelectButtonContent = "Change";
                }
                else
                {
                    //this.tb.Text = "Nothing Selected.";
                    //this.selectButton.Content = "Select";
                    SelectionText = "Nothing Selected";
                    SelectButtonContent = "Select";
                }

                if (dirty)
                    RequiresRecalc = true;
            }
        }

        public override Value Evaluate(FSharpList<Value> args)
        {
            if (SelectedElement == null)
            {
                //if (dynSettings.Controller.Testing)
                //{
                //    //if we're in test mode
                //    //try to pass out an object of the right type
                //    if (this is dynCurvesBySelection)
                //    {
                //        FilteredElementCollector fec = new FilteredElementCollector(dynRevitSettings.Doc.Document);
                //        fec.OfClass(typeof(CurveElement));

                //        if (fec.ToElements().Any())
                //        {
                //            //attempt to find elements that have not yet been selected
                //            //this is important for things like lofts where you cannot have
                //            //an element be the input for two parts of the form
                //            var curveBySelectionNodes = dynSettings.Controller.DynamoModel.Nodes
                //                .Where(x => (x is dynCurvesBySelection) && (x as dynCurvesBySelection).SelectedElement != null);
                //            var previouslySelectedElements = curveBySelectionNodes
                //                .Select(x => (x as dynCurvesBySelection).SelectedElement);
                //            foreach (Element e in fec.ToElements())
                //            {
                //                if (e is ModelCurve)
                //                {
                //                    if (!previouslySelectedElements.Contains(e))
                //                    {
                //                        this.SelectedElement = e;
                //                        break;
                //                    }
                //                }
                //            }
                //        }
                //        else
                //            throw new Exception("Suitable curve could not be found for testing.");
                //    }
                //}
                //else
                throw new Exception("Nothing selected.");
            }

            return Value.NewContainer(SelectedElement);
        }

        public override void SaveElement(XmlDocument xmlDoc, XmlElement dynEl)
        {
            //Debug.WriteLine(pd.Object.GetType().ToString());
            if (SelectedElement != null)
            {
                XmlElement outEl = xmlDoc.CreateElement("instance");
                outEl.SetAttribute("id", SelectedElement.UniqueId);
                dynEl.AppendChild(outEl);
            }
        }

        public override void LoadElement(XmlNode elNode)
        {
            foreach (XmlNode subNode in elNode.ChildNodes)
            {
                if (subNode.Name.Equals("instance"))
                {
                    Element saved = null;
                    var id = subNode.Attributes[0].Value;
                    try
                    {
                        saved = dynRevitSettings.Doc.Document.GetElement(id); // FamilyInstance;
                    }
                    catch
                    {
                        dynSettings.Controller.DynamoViewModel.Log(
                            "Unable to find element with ID: " + id);
                    }
                    SelectedElement = saved;
                }
            }
        }
    }

    [IsInteractive(true)]
    public abstract class dynMultipleElementSelectionBase : dynNodeWithOneOutput
    {
        private TextBox _tb;
        private Button _selectButton;

        protected string _selectButtonContent;

        public string SelectButtonContent
        {
            get { return _selectButtonContent; }
            set
            {
                _selectButtonContent = value;
                RaisePropertyChanged("SelectButtonContent");
            }
        }

        /// <summary>
        /// Determines what the text should read on the node when the selection has been changed.
        /// Is ignored in the case where nothing is selected.
        /// </summary>
        //protected abstract string SelectionText { get; }
        protected string _selectionText;

        public abstract string SelectionText { get; set; }

        protected dynMultipleElementSelectionBase(PortData outData)
        {
            OutPortData.Add(outData);
            RegisterAllPorts();
        }

        public override void SetupCustomUIElements(Controls.dynNodeView nodeUI)
        {
            //add a button to the inputGrid on the dynElement
            _selectButton = new dynNodeButton
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            _selectButton.Click += selectButton_Click;

            _tb = new TextBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false
            };

            if (SelectedElements == null || !SelectedElements.Any() || !SelectionText.Any() || !SelectButtonContent.Any())
            {
                SelectionText = "Nothing Selected";
                SelectButtonContent = "Select Instances";
            }


            nodeUI.inputGrid.RowDefinitions.Add(new RowDefinition());
            nodeUI.inputGrid.RowDefinitions.Add(new RowDefinition());

            nodeUI.inputGrid.Children.Add(_tb);
            nodeUI.inputGrid.Children.Add(_selectButton);

            System.Windows.Controls.Grid.SetRow(_selectButton, 0);
            System.Windows.Controls.Grid.SetRow(_tb, 1);

            _tb.DataContext = this;
            _selectButton.DataContext = this;

            var selectTextBinding = new System.Windows.Data.Binding("SelectionText")
            {
                Mode = BindingMode.TwoWay,
            };
            _tb.SetBinding(TextBox.TextProperty, selectTextBinding);

            var buttonTextBinding = new System.Windows.Data.Binding("SelectButtonContent")
            {
                Mode = BindingMode.TwoWay,
            };
            _selectButton.SetBinding(ContentControl.ContentProperty, buttonTextBinding);
        }

        private void selectButton_Click(object sender, RoutedEventArgs e)
        {
            _selectButton.IsEnabled = false;
            IdlePromise.ExecuteOnIdle(
                delegate
                {
                    OnSelectClick();
                    _selectButton.IsEnabled = true;
                });
        }

        /// <summary>
        /// Callback for when the "Select" button has been clicked.
        /// </summary>
        /// 
        /// 
        protected abstract void OnSelectClick();

        private IList<Element> _selected;

        /// <summary>
        /// The Element which is selected. Setting this property will automatically register the Element
        /// for proper updating, and will update this node's IsDirty value.
        /// </summary>
        public virtual IList<Element> SelectedElements
        {
            get { return _selected; }
            set
            {
                var dirty = false;
                if (_selected != null)
                {
                    foreach (Element selectedElement in _selected)
                    {
                        foreach (Element previousElement in value)
                        {
                            if (previousElement != null
                                && previousElement.Id.Equals(selectedElement.Id))
                                return;

                            dirty = true;
                            this.UnregisterEvalOnModified(selectedElement.Id);
                        }
                    }
                }
                else
                    dirty = value != null;

                _selected = value;
                if (value != null)
                {
                    foreach (Element previousElement in value)
                    {
                        this.RegisterEvalOnModified(
                            previousElement.Id,
                            delAction: delegate
                            {
                                _selected = null;
                                SelectedElements = null;
                            });
                    }

                    //this.tb.Text = this.SelectionText;
                    //this.selectButton.Content = "Change";
                    SelectButtonContent = "Change";
                }
                else
                {
                    //this.tb.Text = "Nothing Selected.";
                    //this.selectButton.Content = "Select";
                    SelectButtonContent = "Select";
                }

                if (dirty)
                    RequiresRecalc = true;
            }
        }

        public override Value Evaluate(FSharpList<Value> args)
        {
            if (SelectedElements == null)
                throw new Exception("Nothing selected.");

            var els = SelectedElements.Select(Value.NewContainer).ToList();

            return Value.NewList(Utils.SequenceToFSharpList(els));
        }

        public override void SaveElement(XmlDocument xmlDoc, XmlElement dynEl)
        {
            //Debug.WriteLine(pd.Object.GetType().ToString());
            if (SelectedElements != null)
            {
                foreach (Element selectedElement in SelectedElements)
                {
                    XmlElement outEl = xmlDoc.CreateElement("instance");
                    outEl.SetAttribute("id", selectedElement.UniqueId);
                    dynEl.AppendChild(outEl);
                }
            }
        }

        public override void LoadElement(XmlNode elNode)
        {
            foreach (XmlNode subNode in elNode.ChildNodes)
            {
                if (subNode.Name.Equals("instance"))
                {
                    Element saved = null;
                    var id = subNode.Attributes[0].Value;
                    try
                    {
                        saved = dynRevitSettings.Doc.Document.GetElement(id);
                    }
                    catch
                    {
                        dynSettings.Controller.DynamoViewModel.Log(
                            "Unable to find element with ID: " + id);
                    }
                    if (SelectedElements == null)
                        SelectedElements = new List<Element>();
                    SelectedElements.Add(saved);
                }
            }
        }
    }

    [NodeName("Select Family Instance")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a family instance from the document.")]
    public class dynFamilyInstanceCreatorSelection : dynElementSelectionBase
    {
        public dynFamilyInstanceCreatorSelection()
            : base(
                new PortData(
                    "fi", "Family instances created by this operation.", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            SelectedElement = dynRevitSettings.SelectionHelper.RequestFamilyInstanceSelection(
                dynRevitSettings.Doc, "Select Massing Family Instance");
            RaisePropertyChanged("SelectionText");
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : SelectedElement.Name;
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }
    }

    [NodeName("Select Divided Surface")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a divided surface from the document.")]
    public class dynDividedSurfaceBySelection : dynElementSelectionBase
    {
        private Value _data;

        public dynDividedSurfaceBySelection()
            : base(
                new PortData(
                    "srf", "The divided surface family instance(s)", typeof(Value.Container))) { }

        public override Value Evaluate(FSharpList<Value> args)
        {
            var result = new List<List<FamilyInstance>>();

            //"Get an interface to the divided surfaces on this element."
            //TODO: do we want to select a face instead and try to get
            //the divided surface that way?
            DividedSurfaceData dsd = SelectedElement.GetDividedSurfaceData();

            if (dsd != null)
            {
                foreach (Reference r in dsd.GetReferencesWithDividedSurfaces())
                {
                    DividedSurface ds = dsd.GetDividedSurfaceForReference(r);

                    var gn = new GridNode();

                    int u = 0;
                    while (u < ds.NumberOfUGridlines)
                    {
                        var lst = new List<FamilyInstance>();

                        gn.UIndex = u;

                        int v = 0;
                        while (v < ds.NumberOfVGridlines)
                        {
                            gn.VIndex = v;

                            //"Reports whether a grid node is a "seed node," a node that is associated with one or more tiles."
                            if (ds.IsSeedNode(gn))
                            {
                                FamilyInstance fi
                                    = ds.GetTileFamilyInstance(gn, 0);

                                //put the family instance into the tree
                                lst.Add(fi);
                            }
                            v = v + 1;
                        }

                        //don't add list if it's empty
                        if (lst.Any())
                            result.Add(lst);

                        u = u + 1;
                    }
                }

                _data = Value.NewList(
                    Utils.SequenceToFSharpList(
                        result.Select(
                            row => Value.NewList(
                                Utils.SequenceToFSharpList(
                                    row.Select(Value.NewContainer))))));
            }

            return _data;
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : "Element ID: " + SelectedElement.Id;
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }

        /*
        public override Element SelectedElement
        {
            get
            {
                return base.SelectedElement;
            }
            set
            {
                base.SelectedElement = value;

                var result = new List<List<FamilyInstance>>();

                DividedSurfaceData dsd = this.SelectedElement.GetDividedSurfaceData();

                if (dsd != null)
                {
                    foreach (Reference r in dsd.GetReferencesWithDividedSurfaces())
                    {
                        DividedSurface ds = dsd.GetDividedSurfaceForReference(r);

                        GridNode gn = new GridNode();

                        int u = 0;
                        while (u < ds.NumberOfUGridlines)
                        {

                            var lst = new List<FamilyInstance>();

                            gn.UIndex = u;

                            int v = 0;
                            while (v < ds.NumberOfVGridlines)
                            {
                                gn.VIndex = v;

                                if (ds.IsSeedNode(gn))
                                {
                                    FamilyInstance fi
                                      = ds.GetTileFamilyInstance(gn, 0);

                                    //put the family instance into the tree
                                    lst.Add(fi);
                                }
                                v = v + 1;
                            }

                            result.Add(lst);

                            u = u + 1;
                        }
                    }

                    this.data = Value.NewList(
                       Utils.SequenceToFSharpList(
                          result.Select(
                             row => Value.NewList(
                                Utils.SequenceToFSharpList(
                                   row.Select(Value.NewContainer)
                                )
                             )
                          )
                       )
                    );
                }
            }
        }
        */

        protected override void OnSelectClick()
        {
            SelectedElement = null;
            SelectedElement = dynRevitSettings.SelectionHelper.RequestFormSelection(
                dynRevitSettings.Doc, "Select a form element.");
            RaisePropertyChanged("SelectionText");
        }
    }

    [NodeName("Select Face")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a face from the document.")]
    public class dynFormElementBySelection : dynElementSelectionBase, IDrawable
    {
        private Reference _f;

        public RenderDescription RenderDescription { get; set; }

        public dynFormElementBySelection()
            : base(new PortData("face", "The face", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            var doc = dynRevitSettings.Doc;

            _f = dynRevitSettings.SelectionHelper.RequestFaceReferenceSelection(
                doc, "Select a face.");
            SelectedElement = doc.Document.GetElement(_f);
            RaisePropertyChanged("SelectionText");
        }

        public override Value Evaluate(FSharpList<Value> args)
        {
            var face =
                (Autodesk.Revit.DB.Face)dynRevitSettings.Doc.Document.GetElement(_f).GetGeometryObjectFromReference(_f);
            return Value.NewContainer(face);
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : "Face ID: " + SelectedElement.Id;
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }

        public void Draw()
        {
            if (RenderDescription == null)
                RenderDescription = new RenderDescription();
            else
                RenderDescription.ClearAll();

            var face =
                (Autodesk.Revit.DB.Face)dynRevitSettings.Doc.Document.GetElement(_f).GetGeometryObjectFromReference(_f);

            dynRevitTransactionNode.DrawFace(RenderDescription, face);
        }

        public override void SaveElement(XmlDocument xmlDoc, XmlElement dynEl)
        {
            if(_f != null)
                dynEl.SetAttribute(
                    "faceRef", _f.ConvertToStableRepresentation(dynRevitSettings.Doc.Document));
        }

        public override void LoadElement(XmlNode elNode)
        {
            try
            {
                _f = Reference.ParseFromStableRepresentation(
                    dynRevitSettings.Doc.Document, elNode.Attributes["faceRef"].Value);
                if (_f != null)
                    SelectedElement = dynRevitSettings.Doc.Document.GetElement(_f.ElementId);
            }
            catch { }
        }
    }

    [NodeName("Select Edge")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select an edge from the document.")]
    public class dynEdgeOnElementBySelection : dynElementSelectionBase, IDrawable
    {
        private Reference _f;
        public RenderDescription RenderDescription { get; set; }

        public dynEdgeOnElementBySelection()
            : base(new PortData("edge", "The edge", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            var doc = dynRevitSettings.Doc;

            _f = dynRevitSettings.SelectionHelper.RequestEdgeReferenceSelection(
                doc, "Select an edge.");
            SelectedElement = doc.Document.GetElement(_f);
            RaisePropertyChanged("SelectionText");
        }

        public override Value Evaluate(FSharpList<Value> args)
        {
            return Value.NewContainer(_f);
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : "Element of Edge  ID: " + SelectedElement.Id;
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }

        public void Draw()
        {
            if (RenderDescription == null)
                RenderDescription = new RenderDescription();
            else
                RenderDescription.ClearAll();

            var edge =
                (Edge)dynRevitSettings.Doc.Document.GetElement(_f).GetGeometryObjectFromReference(_f);

            dynRevitTransactionNode.DrawGeometryElement(RenderDescription, edge);
        }

        public override void SaveElement(XmlDocument xmlDoc, XmlElement dynEl)
        {
            dynEl.SetAttribute(
                "edgeRef", _f.ConvertToStableRepresentation(dynRevitSettings.Doc.Document));
        }

        public override void LoadElement(XmlNode elNode)
        {
            try
            {
                _f = Reference.ParseFromStableRepresentation(
                    dynRevitSettings.Doc.Document, elNode.Attributes["edgeRef"].Value);
                if (_f != null)
                    SelectedElement = dynRevitSettings.Doc.Document.GetElement(_f.ElementId);
            }
            catch { }
        }
    }

    [NodeName("Select Curve")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a curve from the document.")] //or set of curves in the future
    public class dynCurvesBySelection : dynElementSelectionBase, IDrawable
    {
        public dynCurvesBySelection()
            : base(new PortData("curve", "The curve", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            SelectedElement = dynRevitSettings.SelectionHelper.RequestCurveElementSelection(
                dynRevitSettings.Doc, "Select a curve.");
            RaisePropertyChanged("SelectionText");
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : "Curve ID: " + SelectedElement.Id;
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }

        public override void LoadElement(XmlNode elNode)
        {
            foreach (XmlNode subNode in elNode.ChildNodes)
            {
                if (subNode.Name.Equals("instance"))
                {
                    Element saved = null;
                    var id = subNode.Attributes[0].Value;
                    try
                    {
                        saved = dynRevitSettings.Doc.Document.GetElement(id);
                            // FamilyInstance;
                    }
                    catch
                    {
                        dynSettings.Controller.DynamoViewModel.Log(
                            "Unable to find element with ID: " + id);
                    }

                    //only set the selected element if the element
                    //returned is of the type required by this node
                    if (saved is CurveElement)
                        SelectedElement = saved;
                }
            }
        }

        #region IDrawable Interface

        public RenderDescription RenderDescription { get; set; }

        public void Draw()
        {
            if (RenderDescription == null)
                RenderDescription = new RenderDescription();
            else
                RenderDescription.ClearAll();

            var ce = SelectedElement as CurveElement;
            dynRevitTransactionNode.DrawCurve(RenderDescription, ce.GeometryCurve);
        }

        #endregion
    }

    [NodeName("Select Curves")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a set of curves from the document.")]
    public class dynMultipleCurvesBySelection : dynMultipleElementSelectionBase
    {
        public dynMultipleCurvesBySelection()
            : base(new PortData("curves", "The curves", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            if (SelectedElements != null)
            {
                SelectedElements.Clear();
                SelectedElements = null;
            }

            SelectedElements = dynRevitSettings.SelectionHelper
                                               .RequestMultipleCurveElementsSelection(
                                                   dynRevitSettings.Doc, "Select a set of curves.");

            RaisePropertyChanged("SelectionText");
        }

        private static string formatSelectionText(IEnumerable<Element> elements)
        {
            return String.Join(" ", elements.Select(x => x.Id.ToString()));
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = (SelectedElements != null && SelectedElements.Count > 0)
                                            ? "Curve IDs:" + formatSelectionText(SelectedElements)
                                            : "Nothing Selected";
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }
    }

    [NodeName("Select Point")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a reference point from the document.")]
    public class dynPointBySelection : dynElementSelectionBase
    {
        public dynPointBySelection() :
            base(new PortData("pt", "The point", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            SelectedElement = dynRevitSettings.SelectionHelper.RequestReferencePointSelection(
                dynRevitSettings.Doc, "Select a reference point.");

            RaisePropertyChanged("SelectionText");
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : SelectedElement.Name + " ("
                                              + SelectedElement.Id + ")";
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }
    }

    [NodeName("Select Level")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a level from the document.")]
    public class dynLevelBySelection : dynElementSelectionBase
    {
        public dynLevelBySelection() :
            base(new PortData("lvl", "The selected level", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            SelectedElement = dynRevitSettings.SelectionHelper.RequestLevelSelection(
                dynRevitSettings.Doc, "Select a level.");
            RaisePropertyChanged("SelectionText");
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : SelectedElement.Name + " ("
                                              + SelectedElement.Id + ")";
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }
    }

    [NodeName("Select Model Element")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a model element from the document.")]
    public class dynModelElementSelection : dynElementSelectionBase
    {
        public dynModelElementSelection()
            : base(
                new PortData(
                    "me", "Model element reference created by this operation.",
                    typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            SelectedElement = dynRevitSettings.SelectionHelper.RequestModelElementSelection(
                dynRevitSettings.Doc, "Select Model Element");
            RaisePropertyChanged("SelectionText");
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = SelectedElement == null
                                            ? "Nothing Selected"
                                            : SelectedElement.Name;
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }
    }

    [NodeName("Select XYZ on element")]
    [NodeCategory(BuiltinNodeCategories.CORE_SELECTION)]
    [NodeDescription("Select a XYZ location on model face or edge of the element.")]
    public class dynXYZBySelection : dynElementSelectionBase, IDrawable
    {
        private Reference _refXyz;
        private double _param0;
        private double _param1;
        private bool _init;

        public RenderDescription RenderDescription { get; set; }

        public dynXYZBySelection() :
            base(new PortData("XYZ", "The XYZ location on element", typeof(Value.Container))) { }

        protected override void OnSelectClick()
        {
            _refXyz = dynRevitSettings.SelectionHelper.RequestReferenceXYZSelection(
                dynRevitSettings.Doc, "Select a XYZ location on face or edge of the element."
                );
            if (_refXyz != null)
                SelectedElement = dynRevitSettings.Doc.Document.GetElement(_refXyz.ElementId);
            _init = false;

            RaisePropertyChanged("SelectionText");
        }

        public override Value Evaluate(FSharpList<Value> args)
        {
            if (_refXyz.ElementReferenceType != ElementReferenceType.REFERENCE_TYPE_SURFACE &&
                _refXyz.ElementReferenceType != ElementReferenceType.REFERENCE_TYPE_LINEAR)
                throw new Exception("Could not use face or edge which is not part of the model");

            GeometryObject thisObject = SelectedElement.GetGeometryObjectFromReference(_refXyz);
            Autodesk.Revit.DB.Transform thisTrf = null;


            {
                GeometryObject geomObj =
                    SelectedElement.get_Geometry(new Options());
                var geomElement = geomObj as GeometryElement;

                // ugly code to detect if transform for geometry object is needed or not
                // filed request to provide this info via API, but meanwhile ...
                foreach (GeometryObject geob in geomElement)
                {
                    if (!(geob is GeometryInstance))
                        continue;

                    var ginsta = geob as GeometryInstance;
                    GeometryElement gSymbolElement = ginsta.GetSymbolGeometry();
                    var geometryElements = new List<GeometryElement> { gSymbolElement };
                    bool found = false;
                    for (; geometryElements.Count > 0 && !found;)
                    {
                        GeometryElement thisGeometryElement = geometryElements[0];
                        geometryElements.Remove(thisGeometryElement);

                        foreach (GeometryObject geobSym in thisGeometryElement)
                        {
                            if (geobSym is GeometryElement)
                            {
                                geometryElements.Add(geobSym as GeometryElement);
                                continue;
                            }
                            if ((thisObject is Curve) && (geobSym is Curve)
                                && (thisObject == geobSym))
                            {
                                found = true;
                                break;
                            }

                            if (thisObject is Curve)
                                continue;

                            if ((thisObject is Autodesk.Revit.DB.Face) && (geobSym is Autodesk.Revit.DB.Face) && (thisObject == geobSym))
                            {
                                found = true;
                                break;
                            }

                            if ((thisObject is Edge) && (geobSym is Autodesk.Revit.DB.Face))
                            {
                                var edge = thisObject as Edge;
                                //use GetFace after r2013 support is dropped
                                if (geobSym == edge.get_Face(0) || geobSym == edge.get_Face(1))
                                {
                                    found = true;
                                    break;
                                }
                            }
                            if (!(geobSym is Solid))
                                continue;

                            FaceArray solidFaces = ((Solid)geobSym).Faces;
                            int numFaces = solidFaces.Size;
                            for (int index = 0; index < numFaces && !found; index++)
                            {
                                Autodesk.Revit.DB.Face faceAt = solidFaces.get_Item(index);
                                if ((thisObject is Autodesk.Revit.DB.Face) && (thisObject == faceAt))
                                {
                                    found = true;
                                    break;
                                }
                                if (thisObject is Edge)
                                {
                                    var edge = thisObject as Edge;
                                    //use GetFace after r2013 support is dropped
                                    if (faceAt == edge.get_Face(0) || faceAt == edge.get_Face(1))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (found)
                    {
                        thisTrf = ginsta.Transform;
                        break;
                    }
                }
                if (thisObject == null)
                    throw new Exception("could not resolve reference for XYZ on Element");
            }

            XYZ thisXYZ;

            if (_refXyz.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_SURFACE
                && thisObject is Autodesk.Revit.DB.Face)
            {
                var face = thisObject as Autodesk.Revit.DB.Face;
                if (!_init)
                {
                    _param0 = _refXyz.UVPoint[0];
                    _param1 = _refXyz.UVPoint[1];
                    _init = true;
                }
                var uv = new UV(_param0, _param1);
                thisXYZ = face.Evaluate(uv);
                if (thisTrf != null)
                    thisXYZ = thisTrf.OfPoint(thisXYZ);
            }
            else if (_refXyz.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_LINEAR)
            {
                Curve curve;
                if (thisObject is Edge)
                {
                    var edge = (Edge)SelectedElement.GetGeometryObjectFromReference(_refXyz);
                    curve = edge.AsCurve();
                }
                else
                    curve = (Curve)SelectedElement.GetGeometryObjectFromReference(_refXyz);
                if (curve != null)
                {
                    if (_init)
                        thisXYZ = curve.Evaluate(_param0, true);
                    else
                    {
                        XYZ curPoint = _refXyz.GlobalPoint;
                        if (thisTrf != null)
                        {
                            Autodesk.Revit.DB.Transform inverseTrf = thisTrf.Inverse;
                            curPoint = inverseTrf.OfPoint(_refXyz.GlobalPoint);
                        }
                        IntersectionResult thisResult = curve.Project(curPoint);
                        _param0 = curve.ComputeNormalizedParameter(thisResult.Parameter);
                        _init = true;
                    }
                    thisXYZ = curve.Evaluate(_param0, true);
                    _param1 = -1.0;
                }
                else
                    throw new Exception("could not evaluate point on face or edge of the element");
                if (thisTrf != null)
                    thisXYZ = thisTrf.OfPoint(thisXYZ);
            }
            else
                throw new Exception("could not evaluate point on face or edge of the element");

            return Value.NewContainer(thisXYZ);
        }

        public override string SelectionText
        {
            get
            {
                return _selectionText = _refXyz == null
                                            ? "Nothing Selected"
                                            : "Point on element" + " (" + _refXyz.ElementId + ")";
            }
            set
            {
                _selectionText = value;
                RaisePropertyChanged("SelectionText");
            }
        }

        public void Draw()
        {
            if (RenderDescription == null)
                RenderDescription = new RenderDescription();
            else
                RenderDescription.ClearAll();

            XYZ thisXYZ = _refXyz.GlobalPoint;

            dynRevitTransactionNode.DrawXYZ(RenderDescription, thisXYZ);
        }

        public override void SaveElement(XmlDocument xmlDoc, XmlElement dynEl)
        {
            dynEl.SetAttribute(
                "refXYZ", _refXyz.ConvertToStableRepresentation(dynRevitSettings.Doc.Document));
            dynEl.SetAttribute("refXYZparam0", _param0.ToString());
            dynEl.SetAttribute("refXYZparam1", _param1.ToString());
        }

        public override void LoadElement(XmlNode elNode)
        {
            try
            {
                _refXyz = Reference.ParseFromStableRepresentation(
                    dynRevitSettings.Doc.Document, elNode.Attributes["refXYZ"].Value);
                if (_refXyz != null)
                    SelectedElement = dynRevitSettings.Doc.Document.GetElement(
                        _refXyz.ElementId);
                _param0 = Convert.ToDouble(elNode.Attributes["refXYZparam0"].Value);
                _param1 = Convert.ToDouble(elNode.Attributes["refXYZparam1"].Value);
                _init = true;
            }
            catch { }
        }
    }
}