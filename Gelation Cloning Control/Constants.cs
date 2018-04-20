using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gelation_Cloning_Control
{
    static class Constants
    {
        #region Constants for image -> stage correlation. Using Basler pia2400-17gm camera and Prior stage

        //Constant to multiply pixels to stage units. 1 stage unit = 0.04um (smallest increment stage can move in x and y direction). Constant is same in X and Y direction
        public const double pixelsToStage4X = 30.535;

        //Constants for 10X objective. Images are detected in 4X and then laser in 10X. To convert to 10X there is a scale and offset. Offset is in terms of pixels
        //public const double pixlesToStageX10X = 0.5;
        //public const double pixlesToStageY10X = 0.493827;
        public const double pixelsToStage10X = 12.227;
        public const double pixelsToStage4XTo10XConstantOffsetX = 121; //116
        public const double pixelsToStage4XTo10XConstantOffsetY = -126; //-120
        #endregion

        #region Constants for scanning
        public const int moveStage4X_X = -59712;
        public const int moveStage4X_Y = -54453;
        #endregion
    }
}
