using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace Gelation_Cloning_Control
{
    class CellColony
    {
        public double Area { get; set; }
        public Point Centroid { get; set; }
        public Rectangle BoundingBox { get; set; }
        public double NumFluorPixels { get; set; }

        //Default Constructor
        public CellColony()
        {
            Area = 0;
            Centroid = new Point();
            BoundingBox = new Rectangle();
            NumFluorPixels = 0;
        }
        
        public CellColony(double area, Point centroid, Rectangle boundingBox, double numFluorPixels)
        {
            Area = area;
            Centroid = centroid;
            BoundingBox = boundingBox;
            NumFluorPixels = numFluorPixels;
        }
    }
}
