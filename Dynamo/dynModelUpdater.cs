﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Dynamo.Utilities;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.ApplicationServices;

namespace Dynamo
{
   public delegate void DynElementUpdateDelegate(List<ElementId> updated);

   public enum ChangeTypeEnum
   {
      Delete,
      Modified
   };

   public class DynamoUpdater : IUpdater
   {
      static AddInId m_appId;
      static UpdaterId m_updaterId;

      Dictionary<ChangeTypeEnum, Dictionary<ElementId, DynElementUpdateDelegate>> updateDict
         = new Dictionary<ChangeTypeEnum, Dictionary<ElementId, DynElementUpdateDelegate>>();
      
      // constructor takes the AddInId for the add-in associated with this updater
      public DynamoUpdater(AddInId id, ControlledApplication app)
      {
         m_appId = id;
         m_updaterId = new UpdaterId(m_appId, new Guid("1F1F44B4-8002-4CC1-8FDB-17ACD24A2ECE")); //[Guid("1F1F44B4-8002-4CC1-8FDB-17ACD24A2ECE")]

         this.updateDict[ChangeTypeEnum.Delete] = new Dictionary<ElementId, DynElementUpdateDelegate>();
         this.updateDict[ChangeTypeEnum.Modified] = new Dictionary<ElementId, DynElementUpdateDelegate>();

         app.DocumentChanged
            += new EventHandler<DocumentChangedEventArgs>(Application_DocumentChanged);
      }

      private void processUpdates(IEnumerable<ElementId> modified, IEnumerable<ElementId> deleted)
      {
         //Document doc = data.GetDocument();
         var bench = dynElementSettings.SharedInstance.Bench; // MDJ HOOK

         var modDict = this.updateDict[ChangeTypeEnum.Modified];
         var dict = new Dictionary<DynElementUpdateDelegate, List<ElementId>>();
         foreach (ElementId modifiedElementID in modified)
         {
            try
            {
               if (!modDict.ContainsKey(modifiedElementID))
                  continue;

               var k = modDict[modifiedElementID];
               if (!dict.ContainsKey(k))
                  dict[k] = new List<ElementId>();
               dict[k].Add(modifiedElementID);
            }

            catch (Exception e)
            {
               bench.Log(e.ToString());
            }

         }

         foreach (var pair in dict)
         {
            pair.Key(pair.Value);
         }

         modDict = this.updateDict[ChangeTypeEnum.Delete];
         dict.Clear();
         foreach (ElementId deletedElementID in deleted)
         {
            try
            {
               if (!modDict.ContainsKey(deletedElementID))
                  continue;

               var k = modDict[deletedElementID];
               if (!dict.ContainsKey(k))
                  dict[k] = new List<ElementId>();
               dict[k].Add(deletedElementID);

            }

            catch (Exception e)
            {
               bench.Log(e.ToString());
            }

         }

         foreach (var pair in dict)
         {
            pair.Key(pair.Value);
         }
      }

      void Application_DocumentChanged(object sender, DocumentChangedEventArgs args)
      {
         if (args.GetDocument().Equals(dynElementSettings.SharedInstance.Doc.Document))
            this.processUpdates(args.GetModifiedElementIds(), args.GetDeletedElementIds());
      }

      /// <summary>
      /// Watches for changes of the given type to the Element with the given ID. When changed, executes
      /// the given Delegate.
      /// </summary>
      /// <param name="e">ID of the Element being watched.</param>
      /// <param name="type">Type of change to watch for.</param>
      /// <param name="d">Delegate to be called when changed.</param>
      public void RegisterChangeHook(ElementId e, ChangeTypeEnum type, DynElementUpdateDelegate d)
      {
         Dictionary<ElementId, DynElementUpdateDelegate> dict;
         if (!this.updateDict.ContainsKey(type))
         {
            dict = new Dictionary<ElementId, DynElementUpdateDelegate>();
            this.updateDict[type] = dict;
         }
         else
            dict = this.updateDict[type];

         dict[e] = d;
      }

      /// <summary>
      /// Unregisters an element that has been registered via RegisterChangeHook()
      /// </summary>
      /// <param name="e">ID of the Element to unregister.</param>
      /// <param name="type">Type of change to unsubscribe from.</param>
      public void UnRegisterChangeHook(ElementId e, ChangeTypeEnum type)
      {
         this.updateDict[type].Remove(e);
      }

      public void Execute(UpdaterData data)
      {
         this.processUpdates(data.GetModifiedElementIds(), data.GetDeletedElementIds());
      }

      public string GetAdditionalInformation()
      {
         return "Watch for user-selected elements that have been changed or deleted and use this info to update Dynnamo";
      }

      public ChangePriority GetChangePriority()
      {
         return ChangePriority.FloorsRoofsStructuralWalls;
      }

      public UpdaterId GetUpdaterId()
      {
         return m_updaterId;
      }

      public string GetUpdaterName()
      {
         return "Dyanmo Element Watcher";
      }
   }
}