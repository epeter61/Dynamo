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
using System.Linq;
using Autodesk.Revit.DB;
using Dynamo.Connectors;
using Dynamo.Utilities;
using Microsoft.FSharp.Collections;

using Expression = Dynamo.FScheme.Expression;

namespace Dynamo.Elements
{
   [ElementName("XYZ")]
   [ElementCategory(BuiltinElementCategories.REVIT)]
   [ElementDescription("An element which creates an XYZ from three double values.")]
   [RequiresTransaction(false)]
   public class dynXYZ : dynElement
   {
      public dynXYZ()
      {
         InPortData.Add(new PortData("X", "X", typeof(double)));
         InPortData.Add(new PortData("Y", "Y", typeof(double)));
         InPortData.Add(new PortData("Z", "Z", typeof(double)));

         OutPortData = new PortData("xyz", "XYZ", typeof(XYZ));

         base.RegisterInputsAndOutputs();
      }

      public override Expression Evaluate(FSharpList<Expression> args)
      {
         double x, y, z;
         x = (args[0] as Expression.Number).Item;
         y = (args[1] as Expression.Number).Item;
         z = (args[2] as Expression.Number).Item;

         return Expression.NewContainer(new XYZ(x, y, z));
      }
   }


   [ElementName("XYZ Grid")]
   [ElementCategory(BuiltinElementCategories.REVIT)]
   [ElementDescription("An element which creates a grid of reference points.")]
   [RequiresTransaction(true)]
   public class dynReferencePtGrid : dynElement
   {
      public dynReferencePtGrid()
      {
         InPortData.Add(new PortData("x-count", "Number in the X direction.", typeof(double)));
         InPortData.Add(new PortData("y-count", "Number in the Y direction.", typeof(double)));
         InPortData.Add(new PortData("z-count", "Number in the Z direction.", typeof(double)));
         InPortData.Add(new PortData("x0", "Starting X Coordinate", typeof(double)));
         InPortData.Add(new PortData("y0", "Starting Y Coordinate", typeof(double)));
         InPortData.Add(new PortData("z0", "Starting Z Coordinate", typeof(double)));
         InPortData.Add(new PortData("x-space", "The X spacing.", typeof(double)));
         InPortData.Add(new PortData("y-space", "The Y spacing.", typeof(double)));
         InPortData.Add(new PortData("z-space", "The Z spacing.", typeof(double)));

         OutPortData = new PortData("XYZs", "List of XYZs in the grid", typeof(XYZ));

         base.RegisterInputsAndOutputs();
      }

      public override Expression Evaluate(FSharpList<Expression> args)
      {
         double xi, yi, zi, x0, y0, z0, xs, ys, zs;

         xi = ((Expression.Number)args[0]).Item;
         yi = ((Expression.Number)args[1]).Item;
         zi = ((Expression.Number)args[2]).Item;
         x0 = ((Expression.Number)args[3]).Item;
         y0 = ((Expression.Number)args[4]).Item;
         z0 = ((Expression.Number)args[5]).Item;
         xs = ((Expression.Number)args[6]).Item;
         ys = ((Expression.Number)args[7]).Item;
         zs = ((Expression.Number)args[8]).Item;

         FSharpList<Expression> result = FSharpList<Expression>.Empty;

         double z = z0;
         for (int zCount = 0; zCount < zi; zCount++)
         {
            double y = y0;
            for (int yCount = 0; yCount < yi; yCount++)
            {
               double x = x0;
               for (int xCount = 0; xCount < xi; xCount++)
               {
                  result = FSharpList<Expression>.Cons(
                     Expression.NewContainer(new XYZ(x, y, z)),
                     result
                  );
                  x += xs;
               }
               y += ys;
            }
            z += zs;
         }

         return Expression.NewList(
            ListModule.Reverse(result)
         );
      }
   }

   [ElementName("Plane")]
   [ElementCategory(BuiltinElementCategories.REVIT)]
   [ElementDescription("An element which creates a geometric plane.")]
   [RequiresTransaction(false)]
   public class dynPlane : dynElement
   {
      public dynPlane()
      {
         InPortData.Add(new PortData("normal", "Normal Point (XYZ)", typeof(XYZ)));
         InPortData.Add(new PortData("origin", "Origin Point (XYZ)", typeof(XYZ)));
         OutPortData = new PortData("P", "Plane", typeof(Plane));

         base.RegisterInputsAndOutputs();
      }

      public override Expression Evaluate(FSharpList<Expression> args)
      {
         XYZ ptA = (XYZ)((Expression.Container)args[0]).Item;
         XYZ ptB = (XYZ)((Expression.Container)args[1]).Item;

         var plane = this.UIDocument.Application.Application.Create.NewPlane(
            ptA, ptB
         );

         return Expression.NewContainer(plane);
      }
   }

   [ElementName("Sketch Plane")]
   [ElementCategory(BuiltinElementCategories.REVIT)]
   [ElementDescription("An element which creates a geometric sketch plane.")]
   [RequiresTransaction(true)]
   public class dynSketchPlane : dynElement
   {
      public dynSketchPlane()
      {
         InPortData.Add(new PortData("plane", "The plane in which to define the sketch.", typeof(dynPlane)));
         OutPortData = new PortData("SP", "SketchPlane", typeof(dynSketchPlane));

         base.RegisterInputsAndOutputs();
      }

      public override Expression Evaluate(FSharpList<Expression> args)
      {
         Plane p = (Plane)((Expression.Container)args[0]).Item;

         SketchPlane sp;

         //TODO: Handle Removal
         if (this.Elements.Any())
         {
            sp = (SketchPlane)this.Elements[0];
         }

         sp = (this.UIDocument.Document.IsFamilyDocument)
            ? this.UIDocument.Document.FamilyCreate.NewSketchPlane(p)
            : this.UIDocument.Document.Create.NewSketchPlane(p);

         //this.Elements.Add(sp);

         return Expression.NewContainer(sp);
      }
   }

   [ElementName("Line")]
   [ElementCategory(BuiltinElementCategories.REVIT)]
   [ElementDescription("An element which creates a geometric line.")]
   [RequiresTransaction(true)]
   public class dynLineBound : dynElement
   {
      public dynLineBound()
      {
         InPortData.Add(new PortData("start", "Start XYZ", typeof(XYZ)));
         InPortData.Add(new PortData("end", "End XYZ", typeof(XYZ)));
         InPortData.Add(new PortData("bound?", "Boolean: Is this line bounded?", typeof(bool)));
         OutPortData = new PortData("line", "Line", typeof(Line));

         base.RegisterInputsAndOutputs();
      }

      public override Expression Evaluate(FSharpList<Expression> args)
      {
         var ptA = (XYZ)((Expression.Container)args[0]).Item;
         var ptB = (XYZ)((Expression.Container)args[1]).Item;
         var bound = ((Expression.Number)args[2]).Item == 1;

         return Expression.NewContainer(
            this.UIDocument.Application.Application.Create.NewLine(
               ptA, ptB, bound
            )
         );
      }
   }
   [ElementName("UV")]
   [ElementCategory(BuiltinElementCategories.REVIT)]
   [ElementDescription("An element which creates a UV from two double values.")]
   [RequiresTransaction(false)]
   public class dynUV : dynElement
   {
       public dynUV()
       {
           InPortData.Add(new PortData("U", "U", typeof(double)));
           InPortData.Add(new PortData("V", "V", typeof(double)));


           OutPortData = new PortData("uv", "UV", typeof(UV));

           base.RegisterInputsAndOutputs();
       }

       public override FScheme.Expression Evaluate(Microsoft.FSharp.Collections.FSharpList<FScheme.Expression> args)
       {
           double u, v;
           u = (args[0] as FScheme.Expression.Number).Item;
           v = (args[1] as FScheme.Expression.Number).Item;


           return FScheme.Expression.NewContainer(new UV(u, v));
       }
   }
}