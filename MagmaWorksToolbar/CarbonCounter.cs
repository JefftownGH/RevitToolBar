﻿using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace MagmaWorksToolbar
{
    [Transaction(TransactionMode.Manual)]
    public class CarbonCounter : IExternalCommand
    {
        const double _inchesToMm = 25.4;
        const double _footToMm = 12 * _inchesToMm;
        const double _footToM = _footToMm / 1000;
        const double _cubicFtToM = _footToM * _footToM * _footToM;
        const double _squareFtToM = _footToM * _footToM;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            List<string> materialsUsed = new List<string>();
            MaterialsVM myVM = new MaterialsVM();

            var doc = commandData.Application.ActiveUIDocument.Document;
            var cats = new List<BuiltInCategory>{
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_EdgeSlab,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Ramps,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Doors
            };


            //enable volume computations
            using (Transaction t = new Transaction(doc, "Turn on vol calc"))
            {
                t.Start();
                var settings = AreaVolumeSettings.GetAreaVolumeSettings(doc);
                settings.ComputeVolumes = true;
                t.Commit();
            }

            foreach (var cat in cats)
            {
                var catFilters = new List<ElementFilter> { new ElementCategoryFilter(cat) };

                var filter = new LogicalOrFilter(catFilters);

                var structures = new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .ToElements();

                double vol = 0;
                int counter = 0;

                foreach (var item in structures)
                {
                    var mat = item.LookupParameter("Structural Material");
                    if (mat == null)
                    {
                        var itemType = doc.GetElement(item.GetTypeId());
                        if (itemType != null)
                        {
                            mat = itemType.LookupParameter("Structural Material");
                        }
                    }
                    var volParam = item.LookupParameter("Volume");

                    var elemType = item.GetTypeId();
                    string name = "";
                    var elemTy = doc.GetElement(elemType) as ElementType;
                    if (elemTy != null)
                    {
                        name = elemTy.FamilyName + ": " + elemTy.Name;
                    }

                    string matName = "";
                    if (volParam != null)
                    {
                        if (mat != null)
                        {
                            matName = mat.AsValueString();
                            if (!materialsUsed.Contains(matName))
                            {
                                materialsUsed.Add(matName);
                            }
                        }

                        var test = volParam.AsValueString();
                        double metricVol = volParam.AsDouble() * _cubicFtToM;
                        vol += metricVol;
                        counter++;

                        myVM.AddElement(name, metricVol, matName, doc, cat);
                    }
                }
            }

            CarbonCalculator.ElementSet myset = new CarbonCalculator.ElementSet("Category", "Material", "Level", "Phase", "Design Option");

            Window optionsWindow = new Window();
            ImportOptionsVM optionsVM = new ImportOptionsVM(optionsWindow);
            optionsWindow.Content = new CarbonCalculator.ImportOptions();
            optionsWindow.DataContext = optionsVM;
            optionsWindow.Width = 300;
            optionsWindow.Height = 250;
            optionsWindow.ShowDialog();

            int faceCounter = 0;

            foreach (var cat in cats)
            {
                var counter = 0;

                var catFilters = new List<ElementFilter> { new ElementCategoryFilter(cat) };

                var revitCat = Category.GetCategory(doc, cat);

                var filter = new LogicalOrFilter(catFilters);

                var structures = new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .ToElements();

                List<Element> AllElem = new FilteredElementCollector(doc)
                    .WhereElementIsViewIndependent()
                    .WhereElementIsNotElementType()
                    .Where(e => e.IsPhysicalElement())
                    .ToList<Element>();

                double vol = 0;
                foreach (var item in structures)
                {
                    var mat = item.LookupParameter("Structural Material");
                    if (mat == null)
                    {
                        var itemType = doc.GetElement(item.GetTypeId());
                        if (itemType != null)
                        {
                            mat = itemType.LookupParameter("Structural Material");
                        }
                    }

                    var volParam = item.LookupParameter("Volume");

                    var elemType = item.GetTypeId();
                    string name = "";
                    var elemTy = doc.GetElement(elemType) as ElementType;
                    if (elemTy != null)
                    {
                        name = elemTy.FamilyName + ": " + elemTy.Name;
                    }

                    string lvlstr = "";
                    var lvl = item.LevelId;
                    Level level = doc.GetElement(lvl) as Level;
                    if (level != null)
                    {
                        lvlstr = level.Name;
                    }

                    string phaseName = "";
                    Autodesk.Revit.DB.Phase phaseCreated = doc.GetElement(item.CreatedPhaseId) as Phase;
                    if (phaseCreated == null)
                        phaseName = "";
                    else
                        phaseName = phaseCreated.Name;

                    string designOptionName = "";
                    Autodesk.Revit.DB.DesignOption designOption = null;
                    if (item.DesignOption != null)
                    {
                        designOption = doc.GetElement(item.DesignOption.Id) as DesignOption;
                    }                    
                    if (designOption == null)
                    {
                        designOptionName = "Main";
                    }
                    else
                        designOptionName = designOption.Name;

                    var mats = item.GetMaterialIds(false);
                    if (mats.Count > 1 && ((optionsVM.ExplodeFloors && cat == BuiltInCategory.OST_Floors) || (optionsVM.ExplodeWalls && cat == BuiltInCategory.OST_Walls)))
                    {
                        foreach (var material in mats)
                        {
                            var materialName = (doc.GetElement(material) as Material).Name;
                            var materialVol = item.GetMaterialVolume(material);
                            var materialArea = item.GetMaterialArea(material, false);
                            double metricVol = materialVol * _cubicFtToM;
                            double metricArea = materialArea * _squareFtToM;
                            myset.AddElement(new CarbonCalculator.Element(name + " " + materialName, metricVol, metricArea, "Revit" + counter, revitCat.Name, materialName, lvlstr, phaseName, designOptionName));
                            counter++;
                        }
                    }
                    else if (mats.Count == 1)
                    {
                        var material = mats.First();
                        var materialName = (doc.GetElement(material) as Material).Name;
                        var materialVol = item.GetMaterialVolume(material);
                        var materialArea = item.GetMaterialArea(material, false);
                        double metricVol = materialVol * _cubicFtToM;
                        double metricArea = materialArea * _squareFtToM;
                        myset.AddElement(new CarbonCalculator.Element(name, metricVol, metricArea, "Revit" + counter, revitCat.Name, materialName, lvlstr, phaseName, designOptionName));
                        counter++;
                    }
                    else if (mats.Count > 1)
                    {
                        var material = mats.First();
                        var materialName = (doc.GetElement(material) as Material).Name;
                        double metricVol = 0;
                        if (volParam != null)
                        {
                            metricVol = volParam.AsDouble() * _cubicFtToM;
                        }
                        var materialArea = item.GetMaterialArea(material, false); 
                        double metricArea = materialArea * _squareFtToM;
                        myset.AddElement(new CarbonCalculator.Element(name, metricVol, metricArea, "Revit" + counter, revitCat.Name, "Mixed materials", lvlstr, phaseName, designOptionName));
                        counter++;
                    }

                    faceCounter += getGeometry(item);
                    //string matName = "";
                    //if (volParam != null)
                    //{
                    //    if (mat != null)
                    //    {
                    //        matName = mat.AsValueString();
                    //    }

                    //    double metricVol = volParam.AsDouble() * _cubicFtToM;

                    //    myset.AddElement(new CarbonCalculator.Element(name, metricVol, "Revit" + counter, revitCat.Name, matName, lvlstr, phaseName, designOptionName));
                    //}
                    //counter++;

                }
            }

            var control = new CarbonCalculator.UserControl1();
            Window carbonWindow = new Window()
            {
                Content = control,
                DataContext = new CarbonCalculator.AppVM(myset)
            };
            

            carbonWindow.ShowDialog();

            return Result.Succeeded;
        }

        int getGeometry(Element e)
        {
            bool RetainCurvedSurfaceFacets = true;

            Options opt = new Options();
            GeometryElement geo = e.get_Geometry(opt);
            if (geo == null)
                return 0;

            List<int> faceIndices = new List<int>();
            List<double> faceVertices = new List<double>();
            List<double> faceNormals = new List<double>();
            int[] triangleIndices = new int[3];
            XYZ[] triangleCorners = new XYZ[3];

            foreach (GeometryObject obj in geo)
            {
                Solid solid = obj as Solid;

                if (solid != null && 0 < solid.Faces.Size)
                {
                    faceIndices.Clear();
                    faceVertices.Clear();
                    faceNormals.Clear();

                    foreach (Face face in solid.Faces)
                    {
                        Mesh mesh = face.Triangulate();

                        int nTriangles = mesh.NumTriangles;

                        IList<XYZ> vertices = mesh.Vertices;

                        int nVertices = vertices.Count;

                        List<double> vertexCoordsMm = new List<double>(3 * nVertices);

                        // A vertex may be reused several times with 
                        // different normals for different faces, so 
                        // we cannot precalculate normals per vertex.
                        //List<double> normals = new List<double>( 3 * nVertices );

                        foreach (XYZ v in vertices)
                        {
                            // Translate the entire element geometry
                            // to the bounding box midpoint and scale 
                            // to metric millimetres.

                            XYZ p = v;

                            vertexCoordsMm.Add(p.X * _footToMm);
                            vertexCoordsMm.Add(p.Y * _footToMm);
                            vertexCoordsMm.Add(p.Z * _footToMm);
                        }

                        for (int i = 0; i < nTriangles; ++i)
                        {
                            MeshTriangle triangle = mesh.get_Triangle(i);

                            for (int j = 0; j < 3; ++j)
                            {
                                int k = (int)triangle.get_Index(j);
                                triangleIndices[j] = k;
                                triangleCorners[j] = vertices[k];
                            }

                            // Calculate constant triangle facet normal.

                            XYZ v = triangleCorners[1]
                              - triangleCorners[0];
                            XYZ w = triangleCorners[2]
                              - triangleCorners[0];
                            XYZ triangleNormal = v
                              .CrossProduct(w)
                              .Normalize();

                            for (int j = 0; j < 3; ++j)
                            {
                                int nFaceVertices = faceVertices.Count;

                                //Debug.Assert(nFaceVertices.Equals(faceNormals.Count),
                                //  "expected equal number of face vertex and normal coordinates");

                                faceIndices.Add(nFaceVertices / 3);

                                int i3 = triangleIndices[j] * 3;

                                // Rotate the X, Y and Z directions, 
                                // since the Z direction points upward 
                                // in Revit as opposed to sideways or
                                // outwards or forwards in WebGL.

                                faceVertices.Add(vertexCoordsMm[i3 + 1]);
                                faceVertices.Add(vertexCoordsMm[i3 + 2]);
                                faceVertices.Add(vertexCoordsMm[i3]);

                                if (RetainCurvedSurfaceFacets)
                                {
                                    faceNormals.Add(triangleNormal.Y);
                                    faceNormals.Add(triangleNormal.Z);
                                    faceNormals.Add(triangleNormal.X);
                                }
                                else
                                {
                                    UV uv = face.Project(
                                      triangleCorners[j]).UVPoint;

                                    XYZ normal = face.ComputeNormal(uv);

                                    faceNormals.Add(normal.Y);
                                    faceNormals.Add(normal.Z);
                                    faceNormals.Add(normal.X);
                                }
                            }
                        }
                    }

                    // Scale the vertices to a [-1,1] cube 
                    // centered around the origin. Translation
                    // to the origin was already performed above.

                    //double scale = 2.0 / FootToMm(MaxCoord(vsize));

                    //Debug.Print("position: [{0}],",
                    //  string.Join(", ",
                    //    faceVertices.ConvertAll<string>(
                    //      i => (i * scale).ToString("0.##"))));

                    //Debug.Print("normal: [{0}],",
                    //  string.Join(", ",
                    //    faceNormals.ConvertAll<string>(
                    //      f => f.ToString("0.##"))));

                    //Debug.Print("indices: [{0}],",
                    //  string.Join(", ",
                    //    faceIndices.ConvertAll<string>(
                    //      i => i.ToString())));
                }
            }

            return faceIndices.Count;
        }
    }

    public static class Extensions
    {
        public static bool IsPhysicalElement(this Element e)
        {
            if (e.Category == null) return false;
            if (e.ViewSpecific) return false;
            // exclude specific unwanted categories
            if (((BuiltInCategory)e.Category.Id.IntegerValue) == BuiltInCategory.OST_HVAC_Zones) return false;
            //
            return e.Category.CategoryType == CategoryType.Model && e.Category.CanAddSubcategory;
        }
    }
}
