﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Dynamo.Connectors;
using Expression = Dynamo.FScheme.Expression;
using Dynamo.FSchemeInterop;
using Microsoft.FSharp.Collections;

namespace Dynamo.Elements
{
    [ElementName("Surface Area")]
    [ElementCategory(BuiltinElementCategories.MEASUREMENT)]
    [ElementDescription("An element which measures the surface area of a face")]
    [RequiresTransaction(true)]
    public class dynSurfaceArea : dynNode
    {
        public dynSurfaceArea()
        {
            InPortData.Add(new PortData("face", "Ref", typeof(Reference)));//Ref to a face of a form
            OutPortData = new PortData("area", "The surface area of the face.", typeof(object));

            base.RegisterInputsAndOutputs();
        }

        public override Expression Evaluate(FSharpList<Expression> args)
        {
            double area = 0.0;

            object arg0 = ((Expression.Container)args[0]).Item;
            if (arg0 is Reference)
            {
                Reference faceRef = arg0 as Reference;
                Face f = this.UIDocument.Document.GetElement(faceRef.ElementId).GetGeometryObjectFromReference(faceRef) as Face;
                if (f != null)
                {
                    area = f.Area;
                }
            }
            else
            {
                throw new Exception("Cannot cast first argument to Face.");
            }

            //Fin
            return Expression.NewNumber(area);
        }
    }

    [ElementName("Surface Domain")]
    [ElementCategory(BuiltinElementCategories.MEASUREMENT)]
    [ElementDescription("An element which measures the domain of a surface in U and V.")]
    [RequiresTransaction(true)]
    public class dynSurfaceDomain : dynNode
    {
        public dynSurfaceDomain()
        {
            InPortData.Add(new PortData("face", "Ref", typeof(Reference)));//Ref to a face of a form
            OutPortData = new PortData("dom", "The surface area of the face.", typeof(object));

            base.RegisterInputsAndOutputs();
        }

        public override Expression Evaluate(FSharpList<Expression> args)
        {
            double u = 0.0;
            double v = 0.0;

            FSharpList<Expression> result = FSharpList<Expression>.Empty;
           
            object arg0 = ((Expression.Container)args[0]).Item;
            if (arg0 is Reference)
            {
                Reference faceRef = arg0 as Reference;
                Face f = this.UIDocument.Document.GetElement(faceRef.ElementId).GetGeometryObjectFromReference(faceRef) as Face;
                if (f != null)
                {
                    if (!f.get_IsCyclic(0))
                    {
                        u = f.get_Period(0);
                    }

                    if (!f.get_IsCyclic(1))
                    {
                        v = f.get_Period(1);
                    }
                }
            }
            else
            {
                throw new Exception("Cannot cast first argument to Face.");
            }

            FSharpList<Expression>.Cons(
                           Expression.NewNumber(u),
                           result);
            FSharpList<Expression>.Cons(
                           Expression.NewNumber(v),
                           result);
    
            //Fin
            return Expression.NewList(result);
        }
    }
}