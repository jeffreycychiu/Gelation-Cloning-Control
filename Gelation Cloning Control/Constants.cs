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

        //Constants for 10X objective
        public const double pixlesToStageX10X = 0.5;
        public const double pixlesToStageY10X = 0.493827;
        #endregion
    }
}
