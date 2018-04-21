using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using PylonC.NET;
using Basler.Pylon;
using Microsoft.Win32;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.Stitching;
using Emgu.CV.Util;
using Emgu.CV.UI;



namespace Gelation_Cloning_Control
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        SerialPort serialPortArroyo = new SerialPort();
        SerialPort serialPortMicroscopeStage = new SerialPort();

        //Create camera class from basler api
        private Camera camera = null;
        private PixelDataConverter converter = new PixelDataConverter();
        private Stopwatch stopWatch = new Stopwatch();

        //Create the controls for the camera controls
        IFloatParameter exposure = null;
        IIntegerParameter gain = null;

        DispatcherTimer timerUpdateBaslerDeviceList = new DispatcherTimer();
        DispatcherTimer timerUpdateStagePosition = new DispatcherTimer();

        static int CURRENTLIMIT = 6000; //Max current for the laser in milliamps
        static int PERIODLIMIT = 10000; //Max period in milliseconds

        public int zeroX = 0;
        public int zeroY = 0;
        public int zeroZ = 0;

        //The top left/bottom right of the stitched image in terms of the stage coordinates.
        public int stitchedX1 = 0;
        public int stitchedY1 = 0;
        public int stitchedX2 = 0;
        public int stitchedY2 = 0;

        //Set laser offset flag
        public bool offsetFlag = false;
        public int offsetX = 0;
        public int offsetY = 0;

        public Mat stitchedImageBF = new Mat();
        public Mat stitchedImageFluor = new Mat();
        private System.Drawing.Point mouseDownLocation;

        List<double> areasList = new List<double>();
        List<System.Drawing.Point> centroidPointsList = new List<System.Drawing.Point>();
        List<System.Drawing.Rectangle> boundingBoxList = new List<System.Drawing.Rectangle>();
        List<double> numFluorPixels = new List<double>();

        List<CellColony> cellColonies = new List<CellColony>();
        List<PointF> targetCells = new List<PointF>();

        public MainWindow()
        {
            InitializeComponent();
            setSerialPortArroyo();
            setSerialPortMicroscopeStage();

            timerUpdateBaslerDeviceList.Tick += new EventHandler(timerUpdateBaslerDeviceList_Tick);
            timerUpdateBaslerDeviceList.Interval = new TimeSpan(0, 0, 0, 0, 5000);//Not sure how fast this has to be yet
            timerUpdateBaslerDeviceList.IsEnabled = true;

            timerUpdateStagePosition.Tick += new EventHandler(timerUpdateStagePosition_Tick);
            timerUpdateStagePosition.Interval = new TimeSpan(0, 0, 0, 0, 20);//Not sure how fast this has to be yet
            timerUpdateStagePosition.IsEnabled = false;

            UpdateBaslerDeviceList();

            //Disable all buttons
            EnableButtons(false, false);
           
        }

        #region Stage Commands and Connections

        private void cmbBoxSerialPortMicroscopeStage_DropDownOpened(object sender, EventArgs e)
        {
            string[] ports = SerialPort.GetPortNames();
            cmbBoxSerialPortMicroscopeStage.ItemsSource = SerialPort.GetPortNames();
        }

        //Connect to microscope stage serial port
        private void btnConnectMicroscopeStage_Click(object sender, RoutedEventArgs e)
        {
            if (btnConnectMicroscopeStage.Content.ToString() == "Connect Stage")
            {
                try
                {
                    serialPortMicroscopeStage.PortName = cmbBoxSerialPortMicroscopeStage.Text;
                    serialPortMicroscopeStage.Open();
                    btnConnectMicroscopeStage.Content = "Disconnect Stage";
                    //cmbBoxSerialPortMicroscopeStage.IsEnabled = false;

                    //Attempt to send/recieve message - this command recieves the peripherals connected to the controller
                    serialPortMicroscopeStageSend("?");

                    timerUpdateStagePosition.IsEnabled = true;
                    tabStage.IsEnabled = true;

                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex);
                }
            }
            else if (btnConnectMicroscopeStage.Content.ToString() == "Disconnect Stage")
            {
                try
                {
                    serialPortMicroscopeStage.Close();
                    btnConnectMicroscopeStage.Content = "Connect Stage";

                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex);
                }
            }
        }

        //Set serial setting for default of Prior Microscope Stage
        public void setSerialPortMicroscopeStage()
        {
            serialPortMicroscopeStage.BaudRate = 9600;
            serialPortMicroscopeStage.NewLine = "\r";
            serialPortMicroscopeStage.ReadTimeout = 2000;

            serialPortMicroscopeStage.DataReceived += SerialPortMicroscopeStage_DataReceived;
        }

        //Update stage position in XYZ by querying the stage and updating the textboxes
        private void timerUpdateStagePosition_Tick(object sender, EventArgs e)
        {
            if(checkBoxQueryStagePosition.IsChecked == true)
                serialPortMicroscopeStageSend("P");
        }

        //Zero xyz position to current position
        private void btnZeroPosition_Click(object sender, RoutedEventArgs e)
        {
            int xTemp;
            int yTemp;
            int zTemp;
            int.TryParse(textBoxXPosition.Text, out xTemp);
            int.TryParse(textBoxYPosition.Text, out yTemp);
            int.TryParse(textBoxZPosition.Text, out zTemp);

            zeroX = zeroX + xTemp;
            zeroY = zeroY + yTemp;
            zeroZ = zeroZ + zTemp;
        }

        private void btnGoTo_Click(object sender, RoutedEventArgs e)
        {
            if(checkBoxGoToRelative.IsChecked == true)
                serialPortMicroscopeStageSend("GR," + textBoxXGoTo.Text + "," + textBoxYGoto.Text + "," + textBoxZGoTo.Text);
            else
                serialPortMicroscopeStageSend("G," + textBoxXGoTo.Text + "," + textBoxYGoto.Text + "," + textBoxZGoTo.Text);
        }

        //Select folder for the images to be saved during a scanning
        private void btnSaveScanImageFolderPath_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            //saveFileDialog.Filter = "bmp (*.bmp)|*.bmp";
            saveFileDialog.Filter = "tiff (*.tiff)|*.tiff";
            if (saveFileDialog.ShowDialog() == true)
            {
                textBoxSaveScanImageFolderPath.Text = saveFileDialog.FileName;
                //(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image.Save(saveFileDialog.FileName, ImageFormat.Bmp);
            }
        }

        //Stage scanning function
        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            int xFields = 0;
            int yFields = 0;

            int.TryParse(textBoxFieldsX.Text, out xFields);
            int.TryParse(textBoxFieldsY.Text, out yFields);

            string lens;
            int moveStageX = 0;
            int moveStageY = 0;
            lens = comboBoxScanLens.Text;

            switch (lens)
            {
                default:
                    MessageBox.Show("No Lens Selected");
                    break;
                case "4X Nikon":
                    moveStageX = -59712;    //15% overlap. Calculated from entire x field of 4x being 70250 stage units. 70250*0.85 = 59712.5
                    moveStageY = -54453;    //15% overlap. Calculated from entire y field of 4x being 64062.5 stage units. 64062.5*0.85 = 54453.125
                    break;
                case "10X Nikon":
                    moveStageX = -32594;    //15% overlap. Calculated from entire x field of 10X being 38345.8725 stage units (1 stage unit = 0.04um). 38345.8725*0.85 = 32593.99
                    moveStageY = -26957;    //15% overlap. Calculated from entire x field of 10X being 31714.1089 stage units (1 stage unit = 0.04um). 31714.1089*0.85 = 26956.99
                    break;
                case "20X Nikon":
                    moveStageX = -12000;
                    moveStageY = -12000;
                    break;
                case "40X Nikon":
                    moveStageX = -6000;
                    moveStageY = -6000;
                    break;
                case "1550 Aspheric":
                    break;
                case "1064 Microspot Focus Thorlabs":
                    break;
            }

            int[] scanStagePosition = new int[6];
            scanStagePosition = await takePictureWhileScanning(xFields, yFields, moveStageX, moveStageY);

            switch(lens)
            {
                default:
                    MessageBox.Show("No Lens Selected");
                    break;
                case "4X Nikon":
                    stitchedX1 = scanStagePosition[0] + 35125; //Adjust X1 to real top left
                    stitchedY1 = scanStagePosition[1] + 32031; //Adjust Y1 to real top left (should be 32031.25)
                    stitchedX2 = scanStagePosition[3] - 35125; //Adjust X2 to real bot right
                    stitchedY2 = scanStagePosition[4] - 32031; //Adjust Y2 to real top left (should be 32031.25)

                    break;
                case "10X Nikon":
                    stitchedX1 = scanStagePosition[0] + 19173; //Adjust X1 to real top left (38345.8725 / 2 = 19172.93)
                    stitchedY1 = scanStagePosition[1] + 15857; //Adjust Y1 to real top left (31714.1089 / 2 = 15875.05)
                    stitchedX2 = scanStagePosition[3] - 19173; //Adjust X1 to real bot right (38345.8725 / 2 = 19172.93)
                    stitchedY2 = scanStagePosition[4] - 19173; //Adjust X1 to real bot right (38345.8725 / 2 = 19172.93)

                    break;
            }


            //Write position of X1,Y1,Z1, and X2,Y2,Z2
            //Console.WriteLine("ScanStagePosition:");
            //foreach (int num in scanStagePosition)
            //{
            //    Console.WriteLine(num);
            //}

            textBoxX1.Text = stitchedX1.ToString();
            textBoxY1.Text = stitchedY1.ToString();
            textBoxX2.Text = stitchedX2.ToString();
            textBoxY2.Text = stitchedY2.ToString();
        }

        //Async for the timing of the picture taking with the serial commands. The delay time can probably be shortened but this is a safe time
        public async Task<int[]> takePictureWhileScanning(int xFields, int yFields, int moveStageX, int moveStageY)                                                                                                                                                                                                                 
        {
            int picNum = 0;
            int exposureTime;
            //default is 20us for the basler camera. The true time is exposure time * exposure time base. I think this camera is set to be absolute time in microseconds though.
            //Think this parameter should be 4 but to be safe lets make it 10 for a longer delay between pictures
            int exposureTimeBase = 10; 
            int delayTime;
            if (Int32.TryParse(textBoxExposure.Text, out exposureTime))
            {
                delayTime = (exposureTime * exposureTimeBase / 1000);
                if (delayTime < 1500)
                    delayTime = 1500;
                //Console.WriteLine("delayTime: " + delayTime);
            }
            else
            {
                //Console.WriteLine("No exposure time entered");
                delayTime = 1500; //time in milliseconds for camera to stay on target
            }

            //Get X,Y,Z Position of stage on first image
            int xPosFirst = 0, yPosFirst = 0, zPosFirst = 0;
            if (checkBoxQueryStagePosition.IsChecked == true)
            {
                int.TryParse(textBoxXPosition.Text, out xPosFirst);
                int.TryParse(textBoxYPosition.Text, out yPosFirst);
                int.TryParse(textBoxZPosition.Text, out zPosFirst);
            }

            //Create 2d array of images
            Image<Bgr, Byte>[] imageArray = new Image<Bgr, Byte>[xFields * yFields];
            Mat[] mat = new Mat[xFields*yFields];

            //Mat imageMatrix = new Mat()[xFields*yFields];

            for (int row = 0; row < yFields; row++)
            {
                for (int column = 0; column < xFields; column++)
                {
                    await Task.Delay(delayTime);
                    //Save separate bmp images to selected folder
                    if (checkBoxSaveScanImages.IsChecked == true)
                    {
                        string[] filePath = new string[2];
                        filePath = textBoxSaveScanImageFolderPath.Text.Split('.');
                        Bitmap individualImage = (Bitmap)(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image;

                        //Save images to vector of mat then stitch after scanning is complete
                        imageArray[picNum] = new Image<Bgr, Byte>(individualImage);
                        Mat[] split = imageArray[picNum].Mat.Split();
                        mat[picNum] = split[0];
                        mat[picNum].Save(filePath[0] + "-" + picNum.ToString() + "." + filePath[1]);
                        
                        picNum++;
                    }

                    if (column < xFields - 1)
                    { 
                        serialPortMicroscopeStageSend("GR," + moveStageX.ToString() + ",0");
                    }
                }
                if (row < yFields - 1)
                {
                    moveStageX = -moveStageX;
                    serialPortMicroscopeStageSend("GR,0," + moveStageY.ToString());
                }
            }

            //Get X,Y,Z Position of stage on last image
            int xPosLast = 0, yPosLast=  0, zPosLast = 0;
            if (checkBoxQueryStagePosition.IsChecked == true)
            {
                int.TryParse(textBoxXPosition.Text, out xPosLast);
                int.TryParse(textBoxYPosition.Text, out yPosLast);
                int.TryParse(textBoxZPosition.Text, out zPosLast);
            }

            //return to origin location. If yFields is even then there is no need to move in x direction
            await Task.Delay(delayTime);
            if (yFields % 2 == 0)
                serialPortMicroscopeStageSend("GR,0," + (-moveStageY * (yFields-1)).ToString());
            else   
                serialPortMicroscopeStageSend("GR," + (-moveStageX * (xFields-1)).ToString() + "," + (-moveStageY * (yFields-1)).ToString());

            //stitch using imageJ
            if (checkBoxSaveScanImages.IsChecked == true)
            {
                Process process = new Process();
                process.StartInfo.FileName = textBoxImageJFilePath.Text;
                //Pass the arguments separated by commas. Use MicroscopeStitch.ijm ImageJ Macro file to configure stitching parameters
                process.StartInfo.Arguments = "-macro MicroscopeStitch.ijm " + textBoxFieldsX.Text + "," + textBoxFieldsY.Text + "," + textBoxSaveScanImageFolderPath.Text.ToString();
                //Console.WriteLine("Arguments: " + process.StartInfo.Arguments.ToString());
                process.Start();
            }
                int[] positionArray = new int[] { xPosFirst, yPosFirst, zPosFirst, xPosLast, yPosLast, zPosLast };

            return positionArray;
            
        }

        //Load image into the picturebox. Must wait until the stitching has completed!!
        private void btnLoadStitchedImage_Click(object sender, RoutedEventArgs e)
        {
           
            //string imageSaveDirectory = System.IO.Path.GetDirectoryName(textBoxSaveScanImageFolderPath.Text);
            //string stitchedFileName = imageSaveDirectory + "\\stitched.tif";
            string stitchedFileName;

            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
                stitchedFileName = openFileDialog.FileName;
            else
                return;
            //Console.WriteLine(stitchedFileName);

            Mat stitchedImage = CvInvoke.Imread(stitchedFileName, Emgu.CV.CvEnum.LoadImageType.AnyColor);
            Mat displayStitchedImage = new Mat();
            CvInvoke.Resize(stitchedImage, displayStitchedImage, new System.Drawing.Size(1000, 1000), 0, 0, Emgu.CV.CvEnum.Inter.Linear);
            
            //Draw the image on the imagebox. Stop camera first.
            try
            {
                //stop camera
                Stop();
            }
            catch //error if camera not connected - fill in proper exception
            {
                
            }

            Bitmap stitchedImageBitmap = (Bitmap)Bitmap.FromFile(stitchedFileName);
            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image = stitchedImageBitmap;


            
        }

        //Write the position of the mouse into the textboxes
        private void pictureBoxCamera_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            double[] stageConversion = getStageConversionFromObjective(comboBoxScanLens.Text);

            //Pan functionality
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {

                System.Drawing.Point mouseCurrentPosition = e.Location;
                int mouseDeltaX = mouseCurrentPosition.X - mouseDownLocation.X;
                int mouseDeltaY = mouseCurrentPosition.Y - mouseDownLocation.Y;

                int mouseNewX = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Location.X + mouseDeltaX;
                int mouseNewY = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Location.Y + mouseDeltaY;

                (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Location = new System.Drawing.Point(mouseNewX, mouseNewY);
            }


            double mouseXPixel = e.X;
            double mouseYPixel = e.Y;

            int mousePosStageX = 0;
            int mousePosStageY = 0;

            if ((windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image != null)
            {
                //The mouse position clicked is     calculated by: mousePos =  X1 + (X2-X1) * (Xm / w) ; where X1,X2 = stitchedX1 and stitchedX2, Xm = mouse position X, and w = picturebox width
                mousePosStageX = stitchedX1 + (int)Math.Round(((double)(stitchedX2 - stitchedX1) / (double)(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Width) * mouseXPixel);
                mousePosStageY = stitchedY1 + (int)Math.Round(((double)(stitchedY2 - stitchedY1) / (double)(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Height) * mouseYPixel);
            }

            //Add the X and Y offset
            mousePosStageX = mousePosStageX + offsetX;
            mousePosStageY = mousePosStageY + offsetY;

            //Write the stage position to the screen. Convert pixels to stage position
            textBoxMousePositionX.Text = mousePosStageX.ToString();
            textBoxMousePositionY.Text = mousePosStageY.ToString();
        }

        //Write the stage position of the mouse in the picture on click event. 
        //Three different situations: 
        //1)set the offset of the laser (Left or right click)
        //2)Right click: write a list of points to be saved for stage to travel to during lasering
        //3)Left click: Pan image
        private void pictureBoxCamera_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (offsetFlag == true)
            {


                double pictureWidth = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Width;
                double pictureHeight = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Height;
                double imageWidth = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image.Width;
                double imageHeight = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image.Height;

                int mouseX = (int)(e.X * (imageWidth / pictureWidth));
                int mouseY = (int)(e.Y * (imageHeight / pictureHeight));

                //Calculate the offset based on user click position, as well as lens used for lasering
                string laserLens = comboBoxLaserLens.Text;

                switch(laserLens)
                {
                    case "4X Nikon":
                        offsetX = Convert.ToInt32((mouseX - ((double)imageWidth / 2)) * Constants.pixelsToStage4X);
                        offsetY = Convert.ToInt32((mouseY - ((double)imageHeight / 2)) * Constants.pixelsToStage4X);

                        MessageBox.Show("New Offsets Set. X: " + offsetX.ToString() + " ; Y: " + offsetY.ToString());
                        break;
                    case "10X Nikon":
                        offsetX = Convert.ToInt32((mouseX - ((double)imageWidth / 2) + Constants.pixelsToStage4XTo10XConstantOffsetX) * Constants.pixelsToStage10X);
                        offsetY = Convert.ToInt32((mouseY - ((double)imageHeight / 2) + Constants.pixelsToStage4XTo10XConstantOffsetY) * Constants.pixelsToStage10X);

                        MessageBox.Show("New Offsets Set. X: " + offsetX.ToString() + " ; Y: " + offsetY.ToString());
                        break;
                    default:    

                        break;
                }

                offsetFlag = false;
                borderPictureBox.BorderBrush = System.Windows.Media.Brushes.Black;
                borderPictureBox.BorderThickness = new Thickness(2);
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                ListBoxItem point = new ListBoxItem();
                point.Content = textBoxMousePositionX.Text + "," + textBoxMousePositionY.Text;
                listBoxLaserScanPoints.Items.Add(point);
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                //Pan?
            }
        }

        //Implement panning using mousedown
        private void pictureBoxCamera_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Cursor = System.Windows.Forms.Cursors.Hand;
                mouseDownLocation = e.Location;
            }
        }

        private void pictureBoxCamera_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Cursor = System.Windows.Forms.Cursors.Cross;
        }

        //Change the mouse cursor to a cross when in the picturebox
        private void pictureBoxCamera_MouseEnter(object sender, EventArgs e)
        {
            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Cursor = System.Windows.Forms.Cursors.Cross;
            if (!(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Focused)
                (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Focus();
        }

        private void pictureBoxCamera_MouseLeave(object sender, EventArgs e)
        {
            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Cursor = System.Windows.Forms.Cursors.Default;
            if ((windowsFormsHost.Child as System.Windows.Forms.PictureBox).Focused)
                (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Parent.Focus();
        }

        //Implement zooming in picturebox when the mouse wheel is scrolled. Needs to maintain the proper stage x,y positioning for targetting
        private void pictureBoxCamera_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;

            int zoomWidth = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Width;
            int zoomHeight = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Height;

            //Zoom in if delta>0. 25% zoom per scroll
            if (e.Delta > 0)
            {
                zoomWidth = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Width + ((windowsFormsHost.Child as System.Windows.Forms.PictureBox).Width / 4);
                zoomHeight = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Height + ((windowsFormsHost.Child as System.Windows.Forms.PictureBox).Height / 4);
            }
            else if (e.Delta < 0)
            {
                zoomWidth = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Width - ((windowsFormsHost.Child as System.Windows.Forms.PictureBox).Width / 4);
                zoomHeight = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Height - ((windowsFormsHost.Child as System.Windows.Forms.PictureBox).Height / 4);
            }

            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Size = new System.Drawing.Size(zoomWidth, zoomHeight);

        }

        //Delete the selected item in the list box of laser scanning points
        private void btnLaserScanPointDelete_Click(object sender, RoutedEventArgs e)
        {
            listBoxLaserScanPoints.Items.Remove(listBoxLaserScanPoints.SelectedItem);
        }

        //Delete all items in the list box of laser scanning points
        private void btnLaserScanPointClearAll_Click(object sender, RoutedEventArgs e)
        {
            listBoxLaserScanPoints.Items.Clear();
        }

        //Scan all of the points in the laser scanning points listbox
        private async void btnLaserScanPointGo_Click(object sender, RoutedEventArgs e)
        {

            await laserScan((bool)checkBoxActivateLaser.IsChecked);
        } 

        //
        public async Task laserScan(bool activateLaser)
        {
            int delayTime = int.Parse(textBoxDelayTime.Text);    //needs to be adjusted based on laserTime
            List<int[]> scanPoints = new List<int[]>();

            //convert the listbox items into an list of int[2]'s. Each one of those int[2] is the x,y location of where the laser should go.
            foreach (ListBoxItem item in listBoxLaserScanPoints.Items)
            {
                int[] points = item.Content.ToString().Split(',').Select(Int32.Parse).ToArray();
                scanPoints.Add(points);
                
            }

            //Sort list of (X,Y) locations by X. Will decrease the total travel time of the laser than random. Can improve this later by optimizing total path distance.
            scanPoints = scanPoints.OrderByDescending(arr => arr[0]).ThenByDescending(arr => arr[1]).ToList();  

            //foreach(var item in scanPoints)
            //{
            //    Console.WriteLine(item[0] + "/" + item[1]);
            //}

            //Move laser to each point and shoot
            foreach (int[] location in scanPoints)
            {

                //Add X and Y offset for where the laser is centered.
                int xPos = location[0] + offsetX;
                int yPos = location[1] + offsetY;
                
                //move stage to location
                serialPortMicroscopeStageSend("G," + xPos.ToString() + "," + yPos.ToString());
                await Task.Delay(delayTime);
                //turn on laser
                if (checkBoxActivateLaser.IsChecked == true)
                {
                    await firePulsesPWM();
                }
                //wait a certain amount of time
                
                
            }

            listBoxLaserScanPoints.Items.Clear();
        }

        //Hardcoded coordinates to pattern "UBC" in the well. Used for Pre Gel experiments
        private async void btnPatternUBC_Click(object sender, RoutedEventArgs e)
        {
            await PatternUBC(true);
        }

        public async Task PatternUBC(bool param)
        {
            int delayTime = 4000;

            serialPortArroyoSend("LASer:OUTput 1");
            serialPortMicroscopeStageSend("GR,0,-25000");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,-17500,0");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,0,25000");
            await Task.Delay(delayTime);
            
            //Space
            serialPortArroyoSend("LASer:OUTput 0");
            serialPortMicroscopeStageSend("GR,-17500,0");
            await Task.Delay(delayTime);

            //B
            serialPortArroyoSend("LASer:OUTput 1");
            serialPortMicroscopeStageSend("GR,-10000,0");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,0,-12500");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,-7500,0");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,0,-12500");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,17500,0");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,0,12500");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,-10000,0");
            await Task.Delay(delayTime);
            serialPortArroyoSend("LASer:OUTput 0");
            serialPortMicroscopeStageSend("GR,10000,0");
            await Task.Delay(delayTime);
            serialPortArroyoSend("LASer:OUTput 1");
            serialPortMicroscopeStageSend("GR,0,12500");
            await Task.Delay(delayTime);

            //Space
            serialPortArroyoSend("LASer:OUTput 0");
            serialPortMicroscopeStageSend("GR,-17500,0");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,-17500,0");
            await Task.Delay(delayTime);

            //C
            serialPortArroyoSend("LASer:OUTput 1");
            serialPortMicroscopeStageSend("GR,-17500,0");
            await Task.Delay(delayTime);
            serialPortArroyoSend("LASer:OUTput 0");
            serialPortMicroscopeStageSend("GR,17500,0");
            await Task.Delay(delayTime);
            serialPortArroyoSend("LASer:OUTput 1");
            serialPortMicroscopeStageSend("GR,0,-25000");
            await Task.Delay(delayTime);
            serialPortMicroscopeStageSend("GR,-17500,0");
            await Task.Delay(delayTime);
            serialPortArroyoSend("LASer:OUTput 0");
        }
        #endregion

        #region Laser Commands and Connections

        //Fill the combo box with the names of the avaliable serial ports
        private void cmbBoxSerialPortLaser_DropDownOpened(object sender, EventArgs e)
        {
            string[] ports = SerialPort.GetPortNames();
            cmbBoxSerialPortLaser.ItemsSource = SerialPort.GetPortNames();
        }

        //Connect to the laser serial port.
        private void btnConnectLaser_Click(object sender, RoutedEventArgs e)
        {
            if (btnConnectLaser.Content.ToString() == "Connect Laser")
            {
                try
                {
                    serialPortArroyo.PortName = cmbBoxSerialPortLaser.Text;
                    serialPortArroyo.Open();
                    btnConnectLaser.Content = "Disconnect Laser";
                    cmbBoxSerialPortLaser.IsEnabled = false;

                    //Attempt to send/recieve message - get the identification number of the driver
                    serialPortArroyoSend("*IDN?");

                    //Enable buttons & inputs in UI
                    textBoxSerialSendCommandLaser.IsEnabled = true;
                    btnSerialSendCommandLaser.IsEnabled = true;

                    toggleLaser.IsEnabled = true;
                    textBoxCurrentSet.IsEnabled = true;
                    radioBtnCW.IsEnabled = true;
                    radioBtnPWM.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex);
                }
            }
            else if (btnConnectLaser.Content.ToString() == "Disconnect Laser")
            {
                try
                {
                    serialPortArroyo.Close();
                    btnConnectLaser.Content = "Connect Laser";
                    cmbBoxSerialPortLaser.IsEnabled = true;

                    //Disable Buttons & Inputs
                    toggleLaser.IsEnabled = false;
                    textBoxCurrentSet.IsEnabled = false;
                    textBoxSerialSendCommandLaser.IsEnabled = false;
                    btnSerialSendCommandLaser.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex);
                }
            }
        }

        //Set the serial port object parameters (baud rate etc.) to the Arroyo Driver fixed defaults
        public void setSerialPortArroyo()
        {
            serialPortArroyo.BaudRate = 38400;
            serialPortArroyo.Parity = Parity.None;
            serialPortArroyo.DataBits = 8;
            serialPortArroyo.StopBits = StopBits.One;
            serialPortArroyo.Handshake = Handshake.None; //Flow control = none

            serialPortArroyo.DataReceived += SerialPortArroyo_DataReceived;

        }

        //---------------Event Handlers (Laser Commands)---------------

        //Turn the laser on in continuous wave (CW). Return the state of the laser (ON/OFF) after the button is toggledzz
        private void toggleLaser_Click(object sender, RoutedEventArgs e)
        {
            if (radioBtnCW.IsChecked == true && radioBtnPWM.IsChecked == false)
            {
                if (toggleLaser.IsChecked == true)
                {
                    serialPortArroyoSend("LASer:OUTput 1");
                    serialPortArroyoSend("LASer:OUTput?");
                }
                else
                {
                    serialPortArroyoSend("LASer:OUTput 0");
                    serialPortArroyoSend("LASer:OUTput?");
                }
            }
            else if (radioBtnCW.IsChecked == false && radioBtnPWM.IsChecked == true)
            {
                //HANDLE PWM CODE. Create a timer maybe? And then turn things on/off? Use dispatcher timer

            }
            else
            {
                MessageBox.Show("Error: One of Continuous Wave or PWM must be selected");
                toggleLaser.IsChecked = false;
            }

        }

        //Set the current set point in milliamps. Queries the Arroyo laser driver after setting the set point
        private void btnSetCurrent_Click(object sender, RoutedEventArgs e)
        {
            int currentSetPoint = 0;
            if (int.TryParse(textBoxCurrentSet.Text, out currentSetPoint) && currentSetPoint >= 0 && currentSetPoint < CURRENTLIMIT)
            {
                serialPortArroyoSend("LASer:LDI " + currentSetPoint.ToString());
                serialPortArroyoSend("LASer:LDI?");
                btnSetCurrent.IsEnabled = false;
            }
            else
            {
                MessageBox.Show("Error: Current Set Point out of Range or Incorrect Syntax");
                textBoxCurrentSet.Text = 0.ToString();
            }

        }

        //Enable the Set Current Button (btnSetCurrent) when the current is changed
        private void textBoxCurrentSet_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (serialPortArroyo.IsOpen == true)
                btnSetCurrent.IsEnabled = true;
            else
                btnSetCurrent.IsEnabled = false;
        }

        //Make sure that only numbers can be typed into the current set point textbox
        private void textBoxCurrent_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private void radioBtnCW_Checked(object sender, RoutedEventArgs e)
        {
            radioBtnPWM.IsChecked = false;
        }

        //DO I EVEN NEED THIS?
        private void radioBtnCW_Unchecked(object sender, RoutedEventArgs e)
        {
            radioBtnPWM.IsEnabled = true;
        }

        private void radioBtnPWM_Checked(object sender, RoutedEventArgs e)
        {
            radioBtnCW.IsChecked = false;
            textBoxPeriodSet.IsEnabled = true;
            textBoxDutyCycleSet.IsEnabled = true;
            textBoxNumberCycles.IsEnabled = true;
            btnFireCycles.IsEnabled = true;
        }

        private void radioBtnPWM_Unchecked(object sender, RoutedEventArgs e)
        {
            radioBtnCW.IsEnabled = true;
            textBoxPeriodSet.IsEnabled = false;
            textBoxDutyCycleSet.IsEnabled = false;
            textBoxNumberCycles.IsEnabled = false;
            btnSetPeriod.IsEnabled = false;
            btnSetDutyCycle.IsEnabled = false;
            btnSetNumCycles.IsEnabled = false;
            btnFireCycles.IsEnabled = false;
        }

        private void btnSetPeriod_Click(object sender, RoutedEventArgs e)
        {
            int period = 0;
            if (int.TryParse(textBoxPeriodSet.Text, out period) && period >= 0 && period <= PERIODLIMIT)
            {
                //
            }
            else
            {
                MessageBox.Show("Error: Period must be between 0 and " + PERIODLIMIT.ToString());
                textBoxPeriodSet.Text = 0.ToString();
            }
            btnSetDutyCycle.IsEnabled = false;
            btnSetPeriod.IsEnabled = false;
        }

        private void btnSetDutyCycle_Click(object sender, RoutedEventArgs e)
        {
            int dutyCycle;
            if (int.TryParse(textBoxDutyCycleSet.Text, out dutyCycle) && dutyCycle >= 0 && dutyCycle <= 100)
            {
                //
            }
            else
            {
                MessageBox.Show("Error: Duty Cycle must be between 0 and 100");
                textBoxDutyCycleSet.Text = 0.ToString();
            }
            btnSetDutyCycle.IsEnabled = false;
            btnSetNumCycles.IsEnabled = true;
        }

        private void btnSetNumCycles_Click(object sender, RoutedEventArgs e)
        {
            int numCycles;
            if (int.TryParse(textBoxDutyCycleSet.Text, out numCycles) && numCycles >= 0 && numCycles <= 100)
            {
                //
            }
            else
            {
                MessageBox.Show("Error: Number of cycles must be between 0 and 100");
                textBoxDutyCycleSet.Text = 0.ToString();
            }
            btnSetNumCycles.IsEnabled = false;
            btnSetDutyCycle.IsEnabled = false;
        }

        //Fire the amount of pulses as specified by the PWM settings
        private void btnFireCycles_Click(object sender, RoutedEventArgs e)
        {
            firePulsesPWM();
        }

        //pressing the space key also fires the PWM (activates btnFireCycles_Click
        private void pictureBoxCamera_KeyPress(object sender, System.Windows.Forms.KeyPressEventArgs e)
        {
            if ( e.KeyChar == (char)Key.Space)
            {
                firePulsesPWM();
            }
        }

        //Helper function: Fires the amount of pulses as specified by the PWM settings
        public async Task firePulsesPWM ()
        {
            //Create a new timer for this purpose:
            int period = int.Parse(textBoxPeriodSet.Text);
            double dutyCycle = double.Parse(textBoxDutyCycleSet.Text);
            //int numCycles = int.Parse(textBlockNumberCycles.Text);
            int numCycles;
            int.TryParse(textBoxNumberCycles.Text, out numCycles);

            Stopwatch stopwatch = new Stopwatch();

            for (int i = 0; i < numCycles; i++)
            {
                serialPortArroyoSend("LASer:OUTput 1");
                serialPortArroyoSend("LASer:OUTput?");
                
                int onTime = (int)Math.Floor(period * dutyCycle / 100);
                int offTime = period - onTime;
                   
                await Task.Delay(onTime);

                serialPortArroyoSend("LASer:OUTput 0");
                serialPortArroyoSend("LASer:OUTput?");

                await Task.Delay(offTime);
            }
        }

        private void textBoxPeriodSet_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnSetPeriod.IsEnabled = true;
        }

        private void textBoxDutyCycleSet_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnSetDutyCycle.IsEnabled = true;
        }

        private void textBoxNumberCycles_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnSetNumCycles.IsEnabled = true;
        }

        private void textBoxPeriodSet_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private void textBoxDutyCycleSet_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private void textBoxNumberCycles_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        //Set the X,Y Location of the offset. Default is for 10X objective.
        private void btnLaserOffset_Click(object sender, RoutedEventArgs e)
        {
            //Only implement 10X objective for now
            offsetFlag = true;
            borderPictureBox.BorderBrush = System.Windows.Media.Brushes.YellowGreen;
            borderPictureBox.BorderThickness = new Thickness(5);

        }

        #endregion

        #region Camera Commands and Connections

        // Connect to the camera when it is selected in the listbox
        private void listViewCamera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Destroy the old camera object.
            if (camera != null)
            {
                DestroyCamera();
            }

            // Open the connection to the selected camera device.
            if (listViewCamera.SelectedItems.Count > 0)
            {
                // Get the first selected item.
                ListViewItem item = (ListViewItem)listViewCamera.SelectedItem;
                
                // Get the attached device data.
                ICameraInfo selectedCamera = item.Tag as ICameraInfo;
                try
                {
                    // Create a new camera object.
                    camera = new Camera(selectedCamera);

                    camera.CameraOpened += Configuration.AcquireContinuous;

                    // Register for the events of the image provider needed for proper operation.
                    camera.ConnectionLost += OnConnectionLost;
                    camera.CameraOpened += OnCameraOpened;
                    camera.CameraClosed += OnCameraClosed;
                    camera.StreamGrabber.GrabStarted += OnGrabStarted;
                    camera.StreamGrabber.ImageGrabbed += OnImageGrabbed;
                    camera.StreamGrabber.GrabStopped += OnGrabStopped;

                    // Open the connection to the camera device.
                    camera.Open();

                    //Kept the code from example C# code by basler, but the camera I am using (pia2400-17gm) the gain can only be accessed by PLCamera.GainRaw
                    if (camera.Parameters.Contains(PLCamera.GainAbs))
                    {
                        //gain = camera.Parameters[PLCamera.GainAbs];
                    }
                    else
                    {
                        //gain = camera.Parameters[PLCamera.Gain];
                        gain = camera.Parameters[PLCamera.GainRaw];
                    }

                    if (camera.Parameters.Contains(PLCamera.ExposureTimeAbs))
                    {
                        exposure = camera.Parameters[PLCamera.ExposureTimeAbs];
                    }
                    else
                    {
                        exposure = camera.Parameters[PLCamera.ExposureTime];
                    }

                    //Set the textboxes of exposure/gain etc of the default setting
                    textBoxExposure.Text = exposure.GetValue().ToString();
                    textBoxGain.Text = gain.GetValue().ToString();
                }
                catch (Exception exception)
                {
                    ShowException(exception);
                }
            }
        }

        /* Handles the click on the single frame button. */
        private void btnCameraSingleShot_Click(object sender, RoutedEventArgs e)
        {
            OneShot();
        }

        /* Handles the click on the continuous frame button. */
        private void btnCameraContinuousShot_Click(object sender, RoutedEventArgs e)
        {
            ContinuousShot();
        }

        /* Handles the click on the stop frame acquisition button. */
        private void btnCameraStop_Click(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        // Occurs when a device with an opened connection is removed.
        private void OnConnectionLost(Object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                Dispatcher.BeginInvoke(new EventHandler<EventArgs>(OnConnectionLost), sender, e);
                return;
            }

            // Close the camera object.
            DestroyCamera();
            // Because one device is gone, the list needs to be updated.
            UpdateBaslerDeviceList();
        }

        // Occurs when the connection to a camera device is opened.
        private void OnCameraOpened(Object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                Dispatcher.BeginInvoke(new EventHandler<EventArgs>(OnCameraOpened), sender, e);
                return;
            }

            // The image provider is ready to grab. Enable the grab buttons.
            EnableButtons(true, false);
        }

        // Occurs when the connection to a camera device is closed.
        private void OnCameraClosed(Object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                Dispatcher.BeginInvoke(new EventHandler<EventArgs>(OnCameraClosed), sender, e);
                return;
            }

            // The camera connection is closed. Disable all buttons.
            EnableButtons(false, false);
        }

        // Occurs when a camera starts grabbing.
        private void OnGrabStarted(Object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                Dispatcher.BeginInvoke(new EventHandler<EventArgs>(OnGrabStarted), sender, e);
                return;
            }

            // Reset the stopwatch used to reduce the amount of displayed images. The camera may acquire images faster than the images can be displayed.

            stopWatch.Reset();

            // Do not update the device list while grabbing to reduce jitter. Jitter may occur because the GUI thread is blocked for a short time when enumerating.
            timerUpdateBaslerDeviceList.Stop();

            // The camera is grabbing. Disable the grab buttons. Enable the stop button.
            EnableButtons(false, true);
        }

        // Occurs when an image has been acquired and is ready to be processed.
        private void OnImageGrabbed(Object sender, ImageGrabbedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper GUI thread.
                // The grab result will be disposed after the event call. Clone the event arguments for marshaling to the GUI thread.
                Dispatcher.BeginInvoke(new EventHandler<ImageGrabbedEventArgs>(OnImageGrabbed), sender, e.Clone());
                return;
            }

            try
            {
                // Acquire the image from the camera. Only show the latest image. The camera may acquire images faster than the images can be displayed.

                // Get the grab result.
                IGrabResult grabResult = e.GrabResult;

                // Check if the image can be displayed.
                if (grabResult.IsValid)
                {
                    // Reduce the number of displayed images to a reasonable amount if the camera is acquiring images very fast.
                    if (!stopWatch.IsRunning || stopWatch.ElapsedMilliseconds > 33)
                    {
                        stopWatch.Restart();

                        Bitmap bitmap = new Bitmap(grabResult.Width, grabResult.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                        // Lock the bits of the bitmap.
                        BitmapData bmpData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                        // Place the pointer to the buffer of the bitmap.
                        converter.OutputPixelFormat = PixelType.BGRA8packed;
                        IntPtr ptrBmp = bmpData.Scan0;
                        converter.Convert(ptrBmp, bmpData.Stride * bitmap.Height, grabResult); //Exception handling TODO
                        bitmap.UnlockBits(bmpData);

                        // Assign a temporary variable to dispose the bitmap after assigning the new bitmap to the display control.
                        Bitmap bitmapOld = (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image as Bitmap;

                        // Provide the display control with the new bitmap. This action automatically updates the display.
                        (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image = bitmap;

                        if (checkBoxShowCrosshair.IsChecked == true)
                        {
                            Graphics addCrosshair;
                            addCrosshair = Graphics.FromImage(bitmap);
                            System.Drawing.Pen crossHairPen = new System.Drawing.Pen(System.Drawing.Brushes.Red);
                            addCrosshair.DrawLine(crossHairPen, bitmap.Width / 2, bitmap.Height / 2 - 25, bitmap.Width/2, bitmap.Height / 2 + 25);
                            addCrosshair.DrawLine(crossHairPen, (bitmap.Width / 2 - 25), bitmap.Height / 2, bitmap.Width / 2 + 25, bitmap.Height / 2);
                            addCrosshair.Dispose();
                        }

                        if (bitmapOld != null)
                        {
                            // Dispose the bitmap.
                            bitmapOld.Dispose();
                        }
                        
                    }
                }
            }
            catch (Exception exception)
            {
                ShowException(exception);
            }
            finally
            {
                // Dispose the grab result if needed for returning it to the grab loop.
                e.DisposeGrabResultIfClone();
            }
        }

        // Occurs when a camera has stopped grabbing.
        private void OnGrabStopped(Object sender, GrabStopEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                Dispatcher.BeginInvoke(new EventHandler<GrabStopEventArgs>(OnGrabStopped), sender, e);
                return;
            }

            // Reset the stopwatch.
            stopWatch.Reset();

            // Re-enable the updating of the device list.
            timerUpdateBaslerDeviceList.Start();

            // The camera stopped grabbing. Enable the grab buttons. Disable the stop button.
            EnableButtons(true, false);

            // If the grabbed stop due to an error, display the error message.
            if (e.Reason != GrabStopReason.UserRequest)
            {
                MessageBox.Show("A grab error occured:\n" + e.ErrorMessage, "Error");
            }
        }

        // Stops the grabbing of images and handles exceptions.
        private void Stop()
        {
            // Stop the grabbing.
            try
            {
                camera.StreamGrabber.Stop();
            }
            catch (Exception exception)
            {
                ShowException(exception);
            }
        }

        // Closes the camera object and handles exceptions.
        private void DestroyCamera()
        {
            // Disable all parameter controls.
            try
            {
                if (camera != null)
                {
                    
                    //testImageControl.Parameter = null;
                    //pixelFormatControl.Parameter = null;
                    //widthSliderControl.Parameter = null;
                    //heightSliderControl.Parameter = null;
                    //gainSliderControl.Parameter = null;
                    //exposureTimeSliderControl.Parameter = null;
                }
            }
            catch (Exception exception)
            {
                ShowException(exception);
            }

            // Destroy the camera object.
            try
            {
                if (camera != null)
                {
                    camera.Close();
                    camera.Dispose();
                    camera = null;
                }
            }
            catch (Exception exception)
            {
                ShowException(exception);
            }
        }

        // Starts the grabbing of a single image and handles exceptions.
        private void OneShot()
        {
            try
            {
                // Starts the grabbing of one image.
                camera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.SingleFrame);
                camera.StreamGrabber.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);
            }
            catch (Exception exception)
            {
                ShowException(exception);
            }
        }

        // Starts the continuous grabbing of images and handles exceptions.
        private void ContinuousShot()
        {
            try
            {
                // Start the grabbing of images until grabbing is stopped.
                camera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);
                camera.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);
            }
            catch (Exception exception)
            {
                ShowException(exception);
            }
        }

        /* Helps to set the states of Camera buttons. */
        private void EnableButtons(bool canGrab, bool canStop)
        {
            btnCameraSingleShot.IsEnabled = canGrab;
            btnCameraContinuousShot.IsEnabled = canGrab;
            btnCameraStop.IsEnabled = canStop;
            
            textBoxExposure.IsEnabled = canStop;
            textBoxGain.IsEnabled = canStop;
        }

        // Updates the list of available camera devices.
        private void UpdateBaslerDeviceList()
        {
            try
            {
                // Ask the camera finder for a list of camera devices.
                List<ICameraInfo> allCameras = CameraFinder.Enumerate();

                //ListView.ListViewItemCollection items = deviceListView.Items;
                ItemCollection items = listViewCamera.Items;

                // Loop over all cameras found.
                foreach (ICameraInfo cameraInfo in allCameras)
                {
                    // Loop over all cameras in the list of cameras.
                    bool newitem = true;
                    foreach (ListViewItem item in items)
                    {
                        ICameraInfo tag = item.Tag as ICameraInfo;

                        // Is the camera found already in the list of cameras?
                        if (tag[CameraInfoKey.FullName] == cameraInfo[CameraInfoKey.FullName])
                        {
                            tag = cameraInfo;
                            newitem = false;
                            break;
                        }
                    }

                    // If the camera is not in the list, add it to the list.
                    if (newitem)
                    {
                        // Create the item to display.
                        ListViewItem item = new ListViewItem();

                        // Create the tool tip text.
                        string toolTipText = "";
                        foreach (KeyValuePair<string, string> kvp in cameraInfo)
                        {
                            toolTipText += kvp.Key + ": " + kvp.Value + "\n";
                        }
                        item.ToolTip = toolTipText;

                        // Store the camera info in the displayed item. Even with data binding to the name it doesnt work? tested the same code with custom class and its fine but not with listviewitem
                        item.Tag = cameraInfo;
                        item.Name = "BaslerName";
                        item.Content = "BaslerContent";

                        // Attach the device data.
                        listViewCamera.Items.Add(item);
                    }
                }



                // Remove old camera devices that have been disconnected.
                foreach (ListViewItem item in items)
                {
                    bool exists = false;

                    // For each camera in the list, check whether it can be found by enumeration.
                    foreach (ICameraInfo cameraInfo in allCameras)
                    {
                        if (((ICameraInfo)item.Tag)[CameraInfoKey.FullName] == cameraInfo[CameraInfoKey.FullName])
                        {
                            exists = true;
                            break;
                        }
                    }
                    // If the camera has not been found, remove it from the list view.
                    if (!exists)
                    {
                        listViewCamera.Items.Remove(item);
                    }
                }
            }
            catch (Exception exception)
            {
                ShowException(exception);
            }
        }


        //Updates the device list on each timer tick
        private void timerUpdateBaslerDeviceList_Tick(object sender, EventArgs e)
        {
            UpdateBaslerDeviceList();
        }

        //Controls the exposure of the camera
        private void textBoxExposure_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                exposure.ParseAndSetValue(textBoxExposure.Text);
            }
        }

        private void textBoxGain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                int gainInput;
                if (int.TryParse(textBoxGain.Text, out gainInput))
                {
                    if (gainInput > 0 && gainInput <= 500)
                    {
                        gain.SetValue(gainInput);
                    }
                    else
                    {
                        MessageBox.Show("Error: Gain must be between 0 and 500");
                    }
                }
            }
        }

        //Save image to disk
        private void btnSaveImage_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "bmp (*.bmp)|*.bmp";
            if (saveFileDialog.ShowDialog() == true)
            {
                (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image.Save(saveFileDialog.FileName, ImageFormat.Bmp);
            } 
        }

        //Fit the camera image back to the size of the border of the picturebox
        private void btnFitImageToScreen_Click(object sender, RoutedEventArgs e)
        {
            int width = (int)borderPictureBox.ActualWidth;
            int height = (int)borderPictureBox.ActualHeight;
            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Size = new System.Drawing.Size(width, height);
            (windowsFormsHost.Child as System.Windows.Forms.PictureBox).SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
        }
        #endregion

        #region Image Processing

        //Load the stitched brightfield (BF) image into memory
        private void btnLoadImageBF_Click(object sender, RoutedEventArgs e)
        {
            string fileNameImageBF;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
                fileNameImageBF = openFileDialog.FileName;
            else
            {
                MessageBox.Show("Error: No file chosen");
                return;
            }

           stitchedImageBF = CvInvoke.Imread(fileNameImageBF, Emgu.CV.CvEnum.LoadImageType.AnyColor);
           btnLoadImageFluor.IsEnabled = true;
        }

        //Load the stitched flourescent EGFP image into memory
        private void btnLoadImageFluor_Click(object sender, RoutedEventArgs e)
        {
            string fileNameImageEGFP;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
                fileNameImageEGFP = openFileDialog.FileName;
            else
            {
                MessageBox.Show("Error: No file chosen");
                return;
            }

            stitchedImageFluor = CvInvoke.Imread(fileNameImageEGFP, Emgu.CV.CvEnum.LoadImageType.AnyColor);

            //Pad the smaller of the images with 0's to be the same size as larger
            double rowsToPad = Math.Abs(stitchedImageBF.Height - stitchedImageFluor.Height);
            double colsToPad = Math.Abs(stitchedImageBF.Width - stitchedImageFluor.Width);
            
            if (stitchedImageBF.Height < stitchedImageFluor.Height)
            {
                CvInvoke.CopyMakeBorder(stitchedImageBF, stitchedImageBF, (int)Math.Ceiling(rowsToPad/2), (int)Math.Floor(rowsToPad/2), 0, 0, Emgu.CV.CvEnum.BorderType.Constant, new MCvScalar(0));
            }   
            else if (stitchedImageBF.Height > stitchedImageFluor.Height)
            {
                CvInvoke.CopyMakeBorder(stitchedImageFluor, stitchedImageFluor, (int)Math.Ceiling(rowsToPad/2), (int)Math.Floor(rowsToPad / 2), 0, 0, Emgu.CV.CvEnum.BorderType.Constant, new MCvScalar(0));
            }

            if (stitchedImageBF.Width < stitchedImageFluor.Width)
            {
                CvInvoke.CopyMakeBorder(stitchedImageBF, stitchedImageBF, 0, 0, (int)Math.Ceiling(colsToPad / 2), (int)Math.Floor(colsToPad / 2), Emgu.CV.CvEnum.BorderType.Constant, new MCvScalar(0));
            }
            else if (stitchedImageBF.Width > stitchedImageFluor.Width)
            {
                CvInvoke.CopyMakeBorder(stitchedImageFluor, stitchedImageFluor, 0, 0, (int)Math.Ceiling(colsToPad / 2), (int)Math.Floor(colsToPad / 2), Emgu.CV.CvEnum.BorderType.Constant, new MCvScalar(0));
            }
            
            //Register the EGFP image to the BF image

        }

        //Segment and detect cells using the BF image. Return the centroid, number of cells, and segmented region of each cell colony
        private void btnDetectCellsBF_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //Show BF image first
                //ImageViewer imageViewer = new ImageViewer();
                Image<Gray, Byte> imageBF = stitchedImageBF.ToImage<Gray, Byte>();

                //Identify the inner diameter of the well. So we can exclude everything outside of the well
                //The diameter of the well is a constant (depending on the plate used for 96 well plate).
                //The plate I use is ______ for non tissue culture plate and ____ for tissue culture plate (insert model #s)
                //The diameter of the bottom of the well is ___mm and ___mm respectively (using 6.35mm for now)

                //First gaussian blur to reduce noise and avoid false circle detection
                Mat imageGaussianBlur = new Mat();
                CvInvoke.GaussianBlur(imageBF, imageGaussianBlur, new System.Drawing.Size(3, 3), 2, 2);
                //ImageViewer.Show(imageGaussianBlur, "Gaussian Blurred Image");

                //Otsu Threshold to get the threshold to use for Canny detection. This number is also used in the hough circle transform
                Mat otsu = new Mat();
                double otsuThreshold = Emgu.CV.CvInvoke.Threshold(imageBF, otsu, 0, 255, Emgu.CV.CvEnum.ThresholdType.Otsu | Emgu.CV.CvEnum.ThresholdType.Binary);
                //See https://stackoverflow.com/questions/4292249/automatic-calculation-of-low-and-high-thresholds-for-the-canny-operation-in-open for calculation of canny thresholds
                double cannyThresholdLow = otsuThreshold * 0.50;        //Use 0.5 for small cell colonies (cell area detection uses 0.1)
                double cannyThresholdHigh = otsuThreshold;

                //The well diameter detection and removal using hough circle transform disabled for now. It takes up too much memory for some reason. Implement it back in later if neccessary

                //Hough circle transform to find the diameter of the well. Using minRadius = 2575, maxRadius = 2600 for 96 well plate. Will need to change if plate changes
                //Choose the min and max radius depending on which microscope the scanned image was from (user input)
                int minWellRadius = 2575;
                int maxWellRadius = 2600;
                if ( comboBoxMicroscopeSelect.SelectedIndex == 0)   //0 == Mich
                {
                    minWellRadius = 1950;  //1950
                    maxWellRadius = 2050;  //2100
                    Console.WriteLine("Mich");
                }
                else if ( comboBoxMicroscopeSelect.SelectedIndex == 1)  //1 = Leo
                {
                    minWellRadius = 2575;
                    maxWellRadius = 2600;
                    Console.WriteLine("Leo");
                }
                
                
                //Hough circle transform to find large circle. param2 is accumulator threshold for circle centers, smaller means more false circles
                CircleF[] detectedWellCircles = CvInvoke.HoughCircles(imageGaussianBlur, Emgu.CV.CvEnum.HoughType.Gradient, dp: 1, minDist: 10, param1: cannyThresholdHigh, param2:25, minRadius: minWellRadius, maxRadius: maxWellRadius);

                //draw circles onto copied original image
                Gray circleColor = new Gray(255);
                foreach (CircleF circle in detectedWellCircles)
                {
                    //imageBF.Draw(circle, circleColor, 2);
                }

                //ImageViewer.Show(imageBF, "Large Well Diam Circle Drawn");

            
                if (detectedWellCircles.Count() > 0)
                {
                    //Sort circles by descending radius (largest radius first)
                    Array.Sort(detectedWellCircles, delegate (CircleF circle1, CircleF circle2) { return circle2.Radius.CompareTo(circle1.Radius); });
                    int wellRadius = (int)detectedWellCircles[0].Radius;
                    System.Drawing.PointF wellCenter = detectedWellCircles[0].Center;
                
                    //Create a new image of same width/height. Draw a filled circle matching the largest circle found when detecting the well perimeter
                    Mat wellPlateCircleMask = new Mat(imageBF.Size, Emgu.CV.CvEnum.DepthType.Cv32S, 1);         //create empty mat of same size
                    Image<Gray, Byte> wellPlateCircleMaskImage = wellPlateCircleMask.ToImage<Gray, Byte>();
                    wellPlateCircleMaskImage.Draw(detectedWellCircles[0], circleColor, -1);                     //draw a filled circle
                    imageBF = imageBF.And(wellPlateCircleMaskImage);                                            //AND the original image and the well plate circle mask to remove the data outside of the well
                    //ImageViewer.Show(imageBF, "original image AND with well perimeter mask");

                }
                
                

                //Edge detection
                Mat cannyImage = new Mat();

                Console.WriteLine("Canny Thresholds LOW: " + cannyThresholdLow.ToString() + " || HIGH: " + cannyThresholdHigh.ToString());
                Emgu.CV.CvInvoke.Canny(imageBF, cannyImage, cannyThresholdLow, cannyThresholdHigh);
                //ImageViewer.Show(cannyImage, "Canny Edge");

                //Filter out the noise using morphological operations
                //See link for details https://stackoverflow.com/questions/30369031/remove-spurious-small-islands-of-noise-in-an-image-python-opencv
                Mat se1 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(9, 9), new System.Drawing.Point(-1, 1));
                Mat se2 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(5, 5), new System.Drawing.Point(-1, 1));

                Mat mask = new Mat();
                Emgu.CV.CvInvoke.MorphologyEx(cannyImage, mask, Emgu.CV.CvEnum.MorphOp.Close, se1, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));
                Emgu.CV.CvInvoke.MorphologyEx(mask, mask, Emgu.CV.CvEnum.MorphOp.Open, se2, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));
           
                //ImageViewer.Show(mask, "mask");
            
                Image<Gray, Byte> maskImage = mask.ToImage<Gray, Byte>();
                //Image<Gray, Byte> morphologyImage = imageBF.Mul(maskImage);
                //ImageViewer.Show(morphologyImage, "Image after noise filtering mask using morphology operations");

                //Overlay mask with original image.

                Image<Gray, Byte> imageOverlayMask = imageBF.Add(maskImage);
                //ImageViewer.Show(imageOverlayMask, "mask added to original image");
        
                //Find areas and centroid of areas remaining. Remove the small areas (should be noise), and large areas (debris)
                //then return centroids 
                VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
                Mat hiearchy = new Mat();

                Image<Gray, Byte> imageOverlayContours = imageBF;

                Emgu.CV.CvInvoke.FindContours(maskImage, contours, hiearchy, Emgu.CV.CvEnum.RetrType.List, Emgu.CV.CvEnum.ChainApproxMethod.ChainApproxNone);

                Console.WriteLine("Number of contours detected: " + contours.Size);

                //Draw contours on image to visualize
                MCvScalar contourColor = new MCvScalar(0);
                //Emgu.CV.CvInvoke.DrawContours(imageOverlayContours, contours, -1, contourColor, 2);

                //Calculate areas and moments to find centroids
                //double[] areas = new double[contours.Size];
                //System.Drawing.Point[] centroidPoints = new System.Drawing.Point[contours.Size];
                //System.Drawing.Rectangle[] boundingBox = new System.Drawing.Rectangle[contours.Size];

                //List<double> areasList = new List<double>(contours.Size);
                //List<System.Drawing.Point> centroidPointsList = new List<System.Drawing.Point>(contours.Size);
                //List<System.Drawing.Rectangle> boundingBoxList = new List<System.Drawing.Rectangle>(contours.Size);

                Gray centroidColor = new Gray(0);

                for (int i = 0; i < contours.Size; i++)
                {
                    //areas[i] = CvInvoke.ContourArea(contours[i], false);
                    areasList.Add(CvInvoke.ContourArea(contours[i], false));
                    Console.WriteLine(areasList[i]);
                    MCvMoments moment = CvInvoke.Moments(contours[i]);
                    int centroidX, centroidY;
                    if (moment.M00 != 0)
                    {
                        centroidX = Convert.ToInt32(Math.Round(moment.M10 / moment.M00));
                        centroidY = Convert.ToInt32(Math.Round(moment.M01 / moment.M00));
                    }
                    else
                    {
                        break;
                    }

                    //centroidPoints[i] = new System.Drawing.Point(centroidX, centroidY);
                    centroidPointsList.Add(new System.Drawing.Point(centroidX, centroidY));
                    //CircleF centroidVisual = new CircleF(centroidPoints[i], 2);
                    CircleF centroidVisual = new CircleF(centroidPointsList[i], 2);
                    //imageOverlayContours.Draw(centroidVisual, centroidColor, 1);
                    
                    //Get bounding box of each contour
                    //boundingBox[i] = CvInvoke.BoundingRectangle(contours[i]);
                    System.Drawing.Rectangle boundingRectangle = CvInvoke.BoundingRectangle(contours[i]);
                    int inflateWidth = (int)Math.Round((double)boundingRectangle.Width * 1.0);
                    int inflateHeight = (int)Math.Round((double)boundingRectangle.Height * 1.0);
                    boundingRectangle.Inflate(inflateWidth, inflateHeight);
                    //Make sure the bounding rectangle is within the dimensions of the image
                    if (boundingRectangle.X + boundingRectangle.Width > stitchedImageBF.Width)
                    {
                        boundingRectangle.Width = stitchedImageBF.Width - boundingRectangle.X;
                    }
                    else if( boundingRectangle.X < 0)
                    {
                        boundingRectangle.X = 0;
                    }
                    if (boundingRectangle.Y + boundingRectangle.Height > stitchedImageBF.Height)
                    {
                        boundingRectangle.Height = stitchedImageBF.Height - boundingRectangle.Y;
                    }
                    else if( boundingRectangle.Y < 0)
                    {
                        boundingRectangle.Y = 0;
                    }

                    boundingBoxList.Add(boundingRectangle);
                    //imageOverlayContours.Draw(boundingBoxList[i], centroidColor, 1);
                }

                //ImageViewer.Show(imageOverlayContours, "Contour drawn and overlaid on original image");
            
                //Write each image bound by the contour rectangle into a new image array
                //Then determine if each image is a cell colony and what the areas are

            
                //Image<Gray, Byte>[] imageColony = new Image<Gray, Byte>[contours.Size];

                //for (int i = 0; i < contours.Size; i++)
                //{
                //    imageColony[i] = imageBF.Copy(boundingBoxList[i]);
                //}

                //Remove small and large areas. Small areas are areas smaller than one cell. Large areas gets rid of the execcisvley big things incorrectly detected as contours
                int minimumArea = 75;
                int maxArea = 5000;
                System.Drawing.Point[][] contourArray = contours.ToArrayOfArray();

                for (int i = areasList.Count - 1; i >= 0; i--)
                {
                    if (areasList[i] < minimumArea || areasList[i] > maxArea)
                    {
                        areasList.RemoveAt(i);
                        boundingBoxList.RemoveAt(i);
                        centroidPointsList.RemoveAt(i);
                    }
                }

                Image<Gray, Byte> imageOverlayContoursSmallAreasRemoved = imageBF;

                //Draw the centroid and bounding box on picture
                for (int i = 0; i < areasList.Count; i++)
                {
                    CircleF centroid = new CircleF(centroidPointsList[i], 2);
                    imageOverlayContoursSmallAreasRemoved.Draw(centroid, centroidColor, 1);
                    imageOverlayContoursSmallAreasRemoved.Draw(boundingBoxList[i], centroidColor, 1);
                    Console.WriteLine("areas after removed: " + areasList[i].ToString());
                }


                ImageViewer.Show(imageOverlayContoursSmallAreasRemoved, "small areas removed Contour drawn and overlaid on original image");

                //imageOverlayContoursSmallAreasRemoved.Save("C:\\Users\\MDL\\Desktop\\Saved Images\\imageOverlayContoursSmallAreaRemoved.bmp");

                //Not sure if these are neccesary
                //imageBF.Dispose();
                //imageOverlayContours.Dispose();
                //imageOverlayContoursSmallAreasRemoved.Dispose();

                //imageGaussianBlur.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Exception: " + ex.ToString());
                Console.WriteLine(ex.ToString());
            }

        }

        //Detect the antibodies secreted from the cells in the EGFP domain
        private void btnDetectSecretionEGFP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Image<Gray, Byte> imageEGFP = stitchedImageFluor.ToImage<Gray, Byte>();

                //Adaptive threshold 
                int windowSize = 9;
                Image<Gray, Byte> imageAdaptiveThreshold = imageEGFP.ThresholdAdaptive(new Gray(255), Emgu.CV.CvEnum.AdaptiveThresholdType.GaussianC, Emgu.CV.CvEnum.ThresholdType.BinaryInv, windowSize, new Gray(1));
                //ImageViewer.Show(imageAdaptiveThreshold, "image after adaptive threshold");

                //morphological open and close to get rid of noise
                Mat se1 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(3, 3), new System.Drawing.Point(-1, 1));
                Mat se2 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(7, 7), new System.Drawing.Point(-1, 1));

                //ImageViewer.Show(imageAdaptiveThreshold, "Thresholded EGFP image");

                Mat mask = new Mat();
                Emgu.CV.CvInvoke.MorphologyEx(imageAdaptiveThreshold, mask, Emgu.CV.CvEnum.MorphOp.Open, se1, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));
                Emgu.CV.CvInvoke.MorphologyEx(mask, mask, Emgu.CV.CvEnum.MorphOp.Close, se2, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));

                Image<Gray, Byte> maskImage = mask.ToImage<Gray, Byte>();
                //ImageViewer.Show(maskImage, "Mask Image");
                //Image<Gray, Byte> morphologyImage = imageEGFP.Mul(maskImage);
                //ImageViewer.Show(morphologyImage, "Image after noise filtering mask using morphology operations");

                //Overlay mask with BF image
                Image<Gray, Byte> imageBF = stitchedImageBF.ToImage<Gray, Byte>();
                Image<Gray, Byte> imageOverlayMask = imageBF.Add(maskImage);
                //ImageViewer.Show(imageOverlayMask, "mask added to BF image");

                //Create new sub images for each section as defined in ROI using brightfield detection. Count the number of white pixels (255) in each image
                Image<Gray, Byte>[] colonySecretionEGFP = new Image<Gray, Byte>[centroidPointsList.Count()];
                Gray boundingBoxColor = new Gray(0);
                int minNumPixelsEGFP = 300;
                for (int i = 0; i < centroidPointsList.Count(); i++)
                {
                    colonySecretionEGFP[i] = maskImage.Copy(boundingBoxList[i]);
                    int numPixelsEGFP = Emgu.CV.CvInvoke.CountNonZero(colonySecretionEGFP[i]);
                    Console.WriteLine("num segmented EGFP pixels in bounding box " + i + " " + numPixelsEGFP);
                    if (numPixelsEGFP < minNumPixelsEGFP)
                    {
                        //Remove ones with 0? not sure if neccesary. I can just sort maybe
                        
                    }

                    //Draw bounding box and centroid
                    CircleF centroid = new CircleF(centroidPointsList[i], 2);
                    imageOverlayMask.Draw(centroid, boundingBoxColor, 1);
                    imageOverlayMask.Draw(boundingBoxList[i], boundingBoxColor, 1);

                    //Calculate Num EGFP pixels / cell area (cell area defined as bounding box size). If the use bounding box checkbox is not selected, default bounding box size = 1
                    if (checkBoxUseBoundingBox.IsChecked == true)
                    {
                        numFluorPixels.Add(numPixelsEGFP / (boundingBoxList[i].Width * boundingBoxList[i].Height));
                    }
                    else
                    {
                        numFluorPixels.Add(numPixelsEGFP);
                    }
                    
                }
                ImageViewer.Show(imageOverlayMask, "EGFP pixels + bounding Box");


                //double percentageKept = double.Parse(textBoxPercentageKept.Text) / 100;
                //int numColoniesKept = (int)Math.Floor(percentageKept * centroidPointsList.Count);
                int numColoniesKept = int.Parse(textBoxProducersKept.Text);

                Console.WriteLine("Total Number of Colonies Detected: " + centroidPointsList.Count);
                Console.WriteLine("Number of Colonies Kept: " + numColoniesKept);

                //Put all the lists a list of the CellColonies class. This is a crappy way to do this: should have made the classes from the start to use less memory. Fix later
                for (int i = 0; i < centroidPointsList.Count; i++)
                {
                    CellColony cellColony = new CellColony(areasList[i], centroidPointsList[i], boundingBoxList[i], numFluorPixels[i]);
                    cellColonies.Add(cellColony);
                }

                //Sort all lists based on numFluorPixels
                Image<Gray, Byte> imageColoniesKept = imageBF.Add(maskImage);
                var cellColoniesSorted = cellColonies.OrderByDescending(x=>x.NumFluorPixels).ToList();
                for (int i = 0; i < numColoniesKept; i++)
                {
                    imageColoniesKept.Draw(new CircleF(cellColoniesSorted[i].Centroid,2), boundingBoxColor, 2);
                    imageColoniesKept.Draw(cellColoniesSorted[i].BoundingBox, boundingBoxColor, 1);
                }

                ImageViewer.Show(imageColoniesKept, "Colonies kept");

                //imageOverlayMask.Save("C:\\Users\\MDL\\Desktop\\Saved Images\\OverlayEGFPMaskAndBF.bmp");
                //imageColoniesKept.Save("C:\\Users\\MDL\\Desktop\\Saved Images\\imageColoniesKept.bmp");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            
        }

        //Calculate Cell Area
        private void btnCalculateArea_Click(object sender, RoutedEventArgs e)
        {
            //Show BF image first
            Image<Gray, Byte> imageBF = stitchedImageBF.ToImage<Gray, Byte>();

            //Identify the inner diameter of the well. So we can exclude everything outside of the well
            //The diameter of the well is a constant (depending on the plate used for 96 well plate).
            //The plate I use is ______ for non tissue culture plate and ____ for tissue culture plate (insert model #s)
            //The diameter of the bottom of the well is ___mm and ___mm respectively (using 6.35mm for now)

            //First gaussian blur to reduce noise and avoid false circle detection
            Mat imageGaussianBlur = new Mat();
            CvInvoke.GaussianBlur(imageBF, imageGaussianBlur, new System.Drawing.Size(3, 3), 2, 2);
            //ImageViewer.Show(imageGaussianBlur, "Gaussian Blurred Image");

            //Hough circle transform to find the diameter of the well. Using minRadius = 2575, maxRadius = 2600 for 96 well plate. Will need to change if plate changes
            //Choose the min and max radius depending on which microscope the scanned image was from (user input)
            int minWellRadius = 2575;
            int maxWellRadius = 2600;
            if (comboBoxMicroscopeSelect.SelectedIndex == 0)   //0 == Mich
            {
                minWellRadius = 1950;
                maxWellRadius = 2100;
                Console.WriteLine("Mich");
            }
            else if (comboBoxMicroscopeSelect.SelectedIndex == 1)  //1 = Leo
            {
                minWellRadius = 2575;
                maxWellRadius = 2600;
                Console.WriteLine("Leo");
            }

            CircleF[] detectedWellCircles = CvInvoke.HoughCircles(imageGaussianBlur, Emgu.CV.CvEnum.HoughType.Gradient, dp: 1, minDist: 10, param2: 10, minRadius: minWellRadius, maxRadius: maxWellRadius);

            //draw circles onto copied original image
            Gray circleColor = new Gray(255);
            foreach (CircleF circle in detectedWellCircles)
            {
                //imageBF.Draw(circle, circleColor, 2);
            }

            //ImageViewer.Show(imageBF, "Large Well Diam Circle Drawn");


            if (detectedWellCircles.Count() > 0)
            {
                //Sort circles by descending radius (largest radius first)
                Array.Sort(detectedWellCircles, delegate (CircleF circle1, CircleF circle2) { return circle2.Radius.CompareTo(circle1.Radius); });
                int wellRadius = (int)detectedWellCircles[0].Radius;
                System.Drawing.PointF wellCenter = detectedWellCircles[0].Center;

                //Create a new image of same width/height. Draw a filled circle matching the largest circle found when detecting the well perimeter
                Mat wellPlateCircleMask = new Mat(imageBF.Size, Emgu.CV.CvEnum.DepthType.Cv32S, 1);         //create empty mat of same size
                Image<Gray, Byte> wellPlateCircleMaskImage = wellPlateCircleMask.ToImage<Gray, Byte>();
                wellPlateCircleMaskImage.Draw(detectedWellCircles[0], circleColor, -1);                     //draw a filled circle
                imageBF = imageBF.And(wellPlateCircleMaskImage);                                            //AND the original image and the well plate circle mask to remove the data outside of the well
                //ImageViewer.Show(imageBF, "original image AND with well perimeter mask");

            }


            //Edge detection
            Mat cannyImage = new Mat();
            Mat otsu = new Mat();
            double otsuThreshold = Emgu.CV.CvInvoke.Threshold(imageBF, otsu, 0, 255, Emgu.CV.CvEnum.ThresholdType.Otsu | Emgu.CV.CvEnum.ThresholdType.Binary);
            //See https://stackoverflow.com/questions/4292249/automatic-calculation-of-low-and-high-thresholds-for-the-canny-operation-in-open for calculation of canny thresholds
            double cannyThresholdLow = otsuThreshold * 0.10;        //edited this low threshold for the large colonies of cells. When using 0.5 it doesn't detect them
            double cannyThresholdHigh = otsuThreshold;
            Console.WriteLine("Canny Thresholds LOW: " + cannyThresholdLow.ToString() + " || HIGH: " + cannyThresholdHigh.ToString());
            Emgu.CV.CvInvoke.Canny(imageBF, cannyImage, cannyThresholdLow, cannyThresholdHigh);
            ImageViewer.Show(cannyImage, "Canny Edge");

            //Adaptive threshold 
            //int windowSize = 15;
            //imageAdaptiveThreshold = imageBF.ThresholdAdaptive(new Gray(255), Emgu.CV.CvEnum.AdaptiveThresholdType.GaussianC, Emgu.CV.CvEnum.ThresholdType.BinaryInv, windowSize, new Gray(5));
            //ImageViewer.Show(imageBF, "image after adaptive threshold");

            //Filter out the noise using morphological operations
            //See link for details https://stackoverflow.com/questions/30369031/remove-spurious-small-islands-of-noise-in-an-image-python-opencv
            Mat se1 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(9, 9), new System.Drawing.Point(-1, 1));
            Mat se2 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(5, 5), new System.Drawing.Point(-1, 1));

            Mat mask = new Mat();
            Emgu.CV.CvInvoke.MorphologyEx(cannyImage, mask, Emgu.CV.CvEnum.MorphOp.Close, se1, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));
            Emgu.CV.CvInvoke.MorphologyEx(mask, mask, Emgu.CV.CvEnum.MorphOp.Open, se2, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));

            //ImageViewer.Show(mask, "mask");

            Image<Gray, Byte> maskImage = mask.ToImage<Gray, Byte>();
            //Image<Gray, Byte> morphologyImage = imageBF.Mul(maskImage);
            //ImageViewer.Show(morphologyImage, "Image after noise filtering mask using morphology operations");

            //Overlay mask with original image.

            Image<Gray, Byte> imageOverlayMask = imageBF.Add(maskImage);
            ImageViewer.Show(imageOverlayMask, "mask added to original image");

            //Find areas and centroid of areas remaining. Remove the small areas (should be noise), and large areas (debris)
            //then return centroids 
            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            Mat hiearchy = new Mat();

            Image<Gray, Byte> imageOverlayContours = imageBF;

            Emgu.CV.CvInvoke.FindContours(maskImage, contours, hiearchy, Emgu.CV.CvEnum.RetrType.List, Emgu.CV.CvEnum.ChainApproxMethod.ChainApproxNone);

            Console.WriteLine("Number of contours detected: " + contours.Size);

            //Draw contours on image to visualize
            MCvScalar contourColor = new MCvScalar(0);
            Emgu.CV.CvInvoke.DrawContours(imageOverlayContours, contours, -1, contourColor, 2);

            //Calculate areas and moments to find centroids
            double[] areas = new double[contours.Size];
            System.Drawing.Point[] centroidPoints = new System.Drawing.Point[contours.Size];
            System.Drawing.Rectangle[] boundingBox = new System.Drawing.Rectangle[contours.Size];

            Gray centroidColor = new Gray(0);

            for (int i = 0; i < contours.Size; i++)
            {
                areas[i] = (CvInvoke.ContourArea(contours[i], false));
                MCvMoments moment = CvInvoke.Moments(contours[i]);
                int centroidX, centroidY;
                if (moment.M00 != 0)
                {
                    centroidX = Convert.ToInt32(Math.Round(moment.M10 / moment.M00));
                    centroidY = Convert.ToInt32(Math.Round(moment.M01 / moment.M00));
                }
                else
                {
                    break;
                }

                centroidPoints[i] = new System.Drawing.Point(centroidX, centroidY);
                CircleF centroidVisual = new CircleF(centroidPoints[i], 2);
                imageOverlayContours.Draw(centroidVisual, centroidColor, 1);

                //Get bounding box of each contour. Expand by a percentage in case FindContours missed a bit of the cells.
                boundingBox[i] = CvInvoke.BoundingRectangle(contours[i]);
                imageOverlayContours.Draw(boundingBox[i], centroidColor, 1);
            }

            ImageViewer.Show(imageOverlayContours, "Contour drawn and overlaid on original image");

            //Write each image bound by the contour rectangle into a new image array
            //Then determine if each image is a cell colony and what the areas are

            Image<Gray, Byte>[] imageColony = new Image<Gray, Byte>[contours.Size];

            for (int i = 0; i < contours.Size; i++)
            {
                imageColony[i] = imageBF.Copy(boundingBox[i]);
            }
        }


        //Detect the fluorescent cells in stitchedImageFluor
        private void btnDetectCellsFluor_Click(object sender, RoutedEventArgs e)
        {
            Image<Gray, Byte> imageFluor = stitchedImageFluor.ToImage<Gray, Byte>();
            //Threshold
            int userThreshold;
            if (int.TryParse(textBoxFluorThreshold.Text, out userThreshold))
            {
                Emgu.CV.Structure.Gray threshold = new Emgu.CV.Structure.Gray(userThreshold);
                Emgu.CV.Structure.Gray maxIntensity = new Emgu.CV.Structure.Gray(255);
                Image<Gray, Byte> imageThreshold = imageFluor.ThresholdBinary(threshold, maxIntensity);
                ImageViewer.Show(imageThreshold, "image after threshold");

                //Remove small areas using opening/closing morphological methods
                Mat se1 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(7, 7), new System.Drawing.Point(-1, 1));
                Mat se2 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(5, 5), new System.Drawing.Point(-1, 1));

                Mat morphologyMat = new Mat();
                Emgu.CV.CvInvoke.MorphologyEx(imageThreshold, morphologyMat, Emgu.CV.CvEnum.MorphOp.Close, se1, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));
                Emgu.CV.CvInvoke.MorphologyEx(morphologyMat, morphologyMat, Emgu.CV.CvEnum.MorphOp.Open, se2, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));

                Image<Gray, Byte> morphologyImage = morphologyMat.ToImage<Gray, Byte>();
                ImageViewer.Show(morphologyImage, "after morphology");

                Image<Gray, Byte> imageOverlayMask = imageFluor.Add(morphologyImage);
                ImageViewer.Show(imageOverlayMask, "mask added to original image");

                VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
                Mat hiearchy = new Mat();
                
                Emgu.CV.CvInvoke.FindContours(morphologyImage, contours, hiearchy, Emgu.CV.CvEnum.RetrType.List, Emgu.CV.CvEnum.ChainApproxMethod.ChainApproxNone);

                Console.WriteLine("Number of contours detected: " + contours.Size);

                List<CellColony> fluorCells = new List<CellColony>();


                for (int i = 0; i < contours.Size; i++)
                {
                    double cellArea = CvInvoke.ContourArea(contours[i], false);
                    MCvMoments moment = CvInvoke.Moments(contours[i]);
                    int centroidX, centroidY;
                    if (moment.M00 != 0)
                    {
                        centroidX = Convert.ToInt32(Math.Round(moment.M10 / moment.M00));
                        centroidY = Convert.ToInt32(Math.Round(moment.M01 / moment.M00));
                    }
                    else
                    {
                        break;
                    }

                    System.Drawing.Point centroid = new System.Drawing.Point(centroidX, centroidY);

                    //Get bounding box of each contour
                    System.Drawing.Rectangle boundingRectangle = CvInvoke.BoundingRectangle(contours[i]);
                    int inflateWidth = (int)Math.Round((double)boundingRectangle.Width * 1.0);
                    int inflateHeight = (int)Math.Round((double)boundingRectangle.Height * 1.0);
                    boundingRectangle.Inflate(inflateWidth, inflateHeight);
                    //Make sure the bounding rectangle is within the dimensions of the image
                    if (boundingRectangle.X + boundingRectangle.Width > stitchedImageBF.Width)
                    {
                        boundingRectangle.Width = stitchedImageBF.Width - boundingRectangle.X;
                    }
                    else if (boundingRectangle.X < 0)
                    {
                        boundingRectangle.X = 0;
                    }
                    if (boundingRectangle.Y + boundingRectangle.Height > stitchedImageBF.Height)
                    {
                        boundingRectangle.Height = stitchedImageBF.Height - boundingRectangle.Y;
                    }
                    else if (boundingRectangle.Y < 0)
                    {
                        boundingRectangle.Y = 0;
                    }

                    fluorCells.Add(new CellColony(cellArea, centroid, boundingRectangle, 0));
                }

                int minimumArea = 20;
                int maxArea = 5000;
                //Remove small and large areas
                for (int i = fluorCells.Count - 1; i >= 0; i--)
                {
                    if (fluorCells[i].Area < minimumArea || fluorCells[i].Area > maxArea)
                    {
                        fluorCells.RemoveAt(i);
                    }
                }

                Console.WriteLine("Number of cells after small/large area removal: " + fluorCells.Count());

                //Draw contours on image to visualize
                Gray centroidColor = new Gray(128);
                Image<Gray, Byte> imageSmallAreasRemoved = imageThreshold;
                foreach (CellColony cell in fluorCells)
                {
                    CircleF centroid = new CircleF(cell.Centroid, 2);
                    imageSmallAreasRemoved.Draw(centroid, centroidColor, 1);
                    //Add centroid to the list of target cells for laser. Need to convert from pixel location to stage units depending where the well is (found during stitching)
                    int targetXuM = stitchedX1 - (int)(((double)cell.Centroid.X / (double)imageThreshold.Width) * (double)(stitchedX1 - stitchedX2));
                    int targetYuM = stitchedY1 - (int)(((double)cell.Centroid.Y / (double)imageThreshold.Height) * (double)(stitchedY1 - stitchedY2));
                    targetCells.Add(new PointF(targetXuM, targetYuM));
                    Console.WriteLine("X: " + cell.Centroid.X + " Y: " + cell.Centroid.Y);
                    //imageSmallAreasRemoved.Draw(cell.BoundingBox, centroidColor, 1);
                }
                

                ImageViewer.Show(imageSmallAreasRemoved, "Contour drawn and overlaid on original image");

                btnGenerateTarget.IsEnabled = true;
            }
            else
            {
                
            }




        }

        //Edit the cell centroid target locations in targetCells using the info gathered from the scan of the image
        private void btnGenerateTarget_Click(object sender, RoutedEventArgs e)
        {
            targetCells = targetCells.OrderByDescending(x => x.X).ThenByDescending(y => y.Y).ToList();

            //Add converted target cells to list box. Do not add if there is a spot within a 100*0.04 = 4um box
            for (int i = 0; i < targetCells.Count(); i++)
            {
                int targetX = (int)targetCells[i].X;
                int targetY = (int)targetCells[i].Y;

                if (i > 0)
                {
                    int targetXLast = (int)targetCells[i - 1].X;
                    int targetYLast = (int)targetCells[i - 1].Y;

                    if (Math.Abs(targetX - targetXLast) > 100 || Math.Abs(targetY - targetYLast) > 100)
                    {
                        ListBoxItem targetPoint = new ListBoxItem();
                        targetPoint.Content = targetX + "," + targetY;
                        listBoxLaserScanPoints.Items.Add(targetPoint);
                    }
                }
                else
                {
                    ListBoxItem targetPoint = new ListBoxItem();
                    targetPoint.Content = targetX + "," + targetY;
                    listBoxLaserScanPoints.Items.Add(targetPoint);
                }
            }
                    
             
        }

        //Scan and generate the points without stitching. This might work better so we don't get stitching problems
        private async void btnScanAndGenerate_Click(object sender, RoutedEventArgs e)
        {
            await scanAndDetectFluorCells();

            btnGenerateTarget.IsEnabled = true;
        }

        public async Task scanAndDetectFluorCells()
        {
            int exposureTime;
            //default is 20us for the basler camera. The true time is exposure time * exposure time base. I think this camera is set to be absolute time in microseconds though.
            //Think this parameter should be 4 but to be safe lets make it 6 for a longer delay between pictures
            int exposureTimeBase = 6;
            int delayTime;
            if (Int32.TryParse(textBoxExposure.Text, out exposureTime))
            {
                delayTime = (exposureTime * exposureTimeBase / 1000);
                if (delayTime < 1500)
                    delayTime = 1500;
                //Console.WriteLine("delayTime: " + delayTime);
            }
            else
            {
                //Console.WriteLine("No exposure time entered");
                delayTime = 1500; //time in milliseconds for camera to stay on target
            }

            int xFields = int.Parse(textBoxFieldsX.Text);
            int yFields = int.Parse(textBoxFieldsY.Text);
            int moveStageX = Constants.moveStage4X_X;
            int moveStageY = Constants.moveStage4X_Y;

            for (int row = 0; row < yFields; row++)
            {
                for (int column = 0; column < xFields; column++)
                {
                    //Add detected cells to global list
                    await Task.Delay(delayTime);
                    targetCells.AddRange(detectFluorCellsThreshold((Bitmap)(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image, false));

                    if (column < xFields - 1)
                    {
                        serialPortMicroscopeStageSend("GR," + moveStageX.ToString() + ",0");
                    }
                }
                if (row < yFields - 1)
                {
                    moveStageX = -moveStageX;
                    serialPortMicroscopeStageSend("GR,0," + moveStageY.ToString());
                }
            }

            //return to origin location. If yFields is even then there is no need to move in x direction
            await Task.Delay(delayTime);
            if (yFields % 2 == 0)
                serialPortMicroscopeStageSend("GR,0," + (-moveStageY * (yFields - 1)).ToString());
            else
                serialPortMicroscopeStageSend("GR," + (-moveStageX * (xFields - 1)).ToString() + "," + (-moveStageY * (yFields - 1)).ToString());
        }

        //Detect fluorescent cells using thresholding. Returns a list of each cell's (X,Y) location
        private List<PointF> detectFluorCellsThreshold(Bitmap bitmap, bool showImage)
        {
            List<PointF> detectedCells = new List<PointF>();
            
            Image<Gray, Byte> image = new Image<Gray, Byte>(bitmap);

            int userThreshold = int.Parse(textBoxFluorThreshold.Text);
            Emgu.CV.Structure.Gray threshold = new Emgu.CV.Structure.Gray(userThreshold);
            Emgu.CV.Structure.Gray maxIntensity = new Emgu.CV.Structure.Gray(255);
            Image<Gray, Byte> imageThreshold = image.ThresholdBinary(threshold, maxIntensity);
            //ImageViewer.Show(imageThreshold, "image after threshold");

            //Remove small areas using opening/closing morphological methods
            Mat se1 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(5, 5), new System.Drawing.Point(-1, 1));
            Mat se2 = Emgu.CV.CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new System.Drawing.Size(3, 3), new System.Drawing.Point(-1, 1));

            Mat morphologyMat = new Mat();
            Emgu.CV.CvInvoke.MorphologyEx(imageThreshold, morphologyMat, Emgu.CV.CvEnum.MorphOp.Close, se1, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));
            Emgu.CV.CvInvoke.MorphologyEx(morphologyMat, morphologyMat, Emgu.CV.CvEnum.MorphOp.Open, se2, new System.Drawing.Point(-1, 1), 1, Emgu.CV.CvEnum.BorderType.Default, new MCvScalar(1));

            Image<Gray, Byte> morphologyImage = morphologyMat.ToImage<Gray, Byte>();
            //ImageViewer.Show(morphologyImage, "after morphology and threshold");

            Image<Gray, Byte> imageOverlayMask = image.Add(morphologyImage);
            //ImageViewer.Show(imageOverlayMask, "mask added to original image");

            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            Mat hiearchy = new Mat();

            Emgu.CV.CvInvoke.FindContours(morphologyImage, contours, hiearchy, Emgu.CV.CvEnum.RetrType.List, Emgu.CV.CvEnum.ChainApproxMethod.ChainApproxNone);

            Console.WriteLine("Number of contours detected: " + contours.Size);

            List<CellColony> fluorCells = new List<CellColony>();


            for (int i = 0; i < contours.Size; i++)
            {
                double cellArea = CvInvoke.ContourArea(contours[i], false);
                MCvMoments moment = CvInvoke.Moments(contours[i]);
                int centroidX, centroidY;
                if (moment.M00 != 0)
                {
                    centroidX = Convert.ToInt32(Math.Round(moment.M10 / moment.M00));
                    centroidY = Convert.ToInt32(Math.Round(moment.M01 / moment.M00));
                }
                else
                {
                    break;
                }

                System.Drawing.Point centroid = new System.Drawing.Point(centroidX, centroidY);

                //Get bounding box of each contour
                System.Drawing.Rectangle boundingRectangle = CvInvoke.BoundingRectangle(contours[i]);

                //int inflateWidth = (int)Math.Round((double)boundingRectangle.Width * 1.0);
                //int inflateHeight = (int)Math.Round((double)boundingRectangle.Height * 1.0);
                //boundingRectangle.Inflate(inflateWidth, inflateHeight);
                ////Make sure the bounding rectangle is within the dimensions of the image
                //if (boundingRectangle.X + boundingRectangle.Width > stitchedImageBF.Width)
                //{
                //    boundingRectangle.Width = stitchedImageBF.Width - boundingRectangle.X;
                //}
                //else if (boundingRectangle.X < 0)
                //{
                //    boundingRectangle.X = 0;
                //}
                //if (boundingRectangle.Y + boundingRectangle.Height > stitchedImageBF.Height)
                //{
                //    boundingRectangle.Height = stitchedImageBF.Height - boundingRectangle.Y;
                //}
                //else if (boundingRectangle.Y < 0)
                //{
                //    boundingRectangle.Y = 0;
                //}

                fluorCells.Add(new CellColony(cellArea, centroid, boundingRectangle, 0));
            }

            int minimumArea = 20;
            int maxArea = 1000;
            //Remove small and large areas
            for (int i = fluorCells.Count - 1; i >= 0; i--)
            {
                //Console.WriteLine("Area: " + fluorCells[i].Area);
                if (fluorCells[i].Area < minimumArea || fluorCells[i].Area > maxArea)
                {
                    fluorCells.RemoveAt(i);
                }
            }

            Console.WriteLine("Number of cells after small/large area removal: " + fluorCells.Count());

            //Draw contours on image to visualize
            Gray centroidColor = new Gray(150);
            Image<Gray, Byte> imageSmallAreasRemoved = image.Add(imageThreshold);
            foreach (CellColony cell in fluorCells)
            {
                CircleF centroid = new CircleF(cell.Centroid, 2);
                imageSmallAreasRemoved.Draw(centroid, centroidColor, 1);
                //Add centroid to the list of target cells for laser. To convert from 4X pixels -> stage units, use 30.535 multiplier
                double targetXStage = Math.Round(double.Parse(textBoxXPosition.Text) - ((cell.Centroid.X - (double)image.Width / 2) * Constants.pixelsToStage4X));
                double targetYStage = Math.Round(double.Parse(textBoxYPosition.Text) - ((cell.Centroid.Y - (double)image.Height / 2) * Constants.pixelsToStage4X));
                detectedCells.Add(new PointF((int)targetXStage, (int)targetYStage));
                Console.WriteLine("X: " + cell.Centroid.X + " Y: " + cell.Centroid.Y);
                imageSmallAreasRemoved.Draw(cell.BoundingBox, centroidColor, 1);
            }

            if (showImage == true)
            {
                ImageViewer.Show(imageSmallAreasRemoved, "Centroid drawn and overlaid on original image");
            }

            image.Dispose();
            //bitmap.Dispose();

            return detectedCells;
        }

        private void btnTestThreshold_Click(object sender, RoutedEventArgs e)
        {
            detectFluorCellsThreshold((Bitmap)(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image, true);
        }
        #endregion

        #region Serial Event Handlers
        //Write the data recieved from the Arroyo instrument to the listbox. Helpful for debugging
        private void SerialPortArroyo_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string recievedData = serialPortArroyo.ReadExisting();
            this.Dispatcher.Invoke(() =>
            {
                listBoxSerialRecievedLaser.Items.Add(recievedData);
                listBoxSerialRecievedLaser.SelectedIndex = listBoxSerialRecievedLaser.Items.Count - 1;
                listBoxSerialRecievedLaser.ScrollIntoView(listBoxSerialRecievedLaser.Items);

                Console.WriteLine("sel index " + listBoxSerialRecievedLaser.SelectedIndex);
                Console.WriteLine("sel item " + listBoxSerialRecievedLaser.SelectedItem);
            });
        }

        //Write the data recieved from the Mircoscope stage to the listbox. Helpful for debugging
        private void SerialPortMicroscopeStage_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string recievedData = serialPortMicroscopeStage.ReadExisting();

            //Put recieved data string into list box in the serial page
            this.Dispatcher.Invoke(() =>
            {
                listBoxSerialRecievedMicroscopeStage.Items.Add(recievedData);
                listBoxSerialRecievedMicroscopeStage.SelectedIndex = listBoxSerialRecievedMicroscopeStage.Items.Count - 1;
                listBoxSerialRecievedMicroscopeStage.ScrollIntoView(listBoxSerialRecievedMicroscopeStage.Items);
            });

            //Parse the recieved data from string into int
            int numValues = 0;
            int[] positions = new int[3];
            foreach (var positionString in recievedData.Split(','))
            {
                if (numValues < 3)
                {
                    int.TryParse(positionString, out positions[numValues]);
                    numValues++;
                }
            }

            if (numValues == 3)
            {
                this.Dispatcher.Invoke(() =>
                {
                    int xPosition = positions[0] - zeroX;
                    int yPosition = positions[1] - zeroY;
                    int zPosition = positions[2] - zeroZ;

                    textBoxXPosition.Text = xPosition.ToString();
                    textBoxYPosition.Text = yPosition.ToString();
                    textBoxZPosition.Text = zPosition.ToString();
                });
            }
            //Console.WriteLine("Data Received from Microscope Stage: " + recievedData);
        }

        private void btnSerialSendCommandLaser_Click(object sender, RoutedEventArgs e)
        {
            serialPortArroyoSend(textBoxSerialSendCommandLaser.Text);
        }

        private void btnSerialSendCommandMicroscopeStage_Click(object sender, RoutedEventArgs e)
        {
            serialPortMicroscopeStageSend(textBoxSerialSendCommandMicroscopeStage.Text);
        }

        #endregion

        #region Targeting Functions
        //Set the position of the stitched image. (X1,Y1) top left and (X2,Y2) bot right. In terms of stage coordinates
        private void btnSetTargetPosition_Click(object sender, RoutedEventArgs e)
        {
            int.TryParse(textBoxX1.Text, out stitchedX1);
            int.TryParse(textBoxY1.Text, out stitchedY1);
            int.TryParse(textBoxX2.Text, out stitchedX2);
            int.TryParse(textBoxY2.Text, out stitchedY2);

            MessageBox.Show("Updated (X1,Y1) and (X2,Y2)");
        }
        #endregion

        #region Other Event Handlers

        //Release the driver so that you can reconnect to the camera again when you re-open the program
        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //PylonC.NET.Pylon.Terminate();
            DestroyCamera();
        }
        #endregion

        #region Helper Functions (serial send, text checking, etc.)

        //Send serial port data to the Arroyo. Automatically appends an endline "\n" character
        private void serialPortArroyoSend(string command)
        {
            listBoxSerialSentLaser.Items.Add(command);
            listBoxSerialSentLaser.SelectedIndex = listBoxSerialSentLaser.Items.Count - 1;
            listBoxSerialSentLaser.ScrollIntoView(listBoxSerialSentLaser.SelectedItem);

            Console.WriteLine(listBoxSerialSentLaser.Items.Count - 1);

            serialPortArroyo.Write(command + "\n"); //Requires newline to send
        }

        private void serialPortMicroscopeStageSend(string command)
        {

            listBoxSerialSentMicroscopeStage.Items.Add(command);
            listBoxSerialSentMicroscopeStage.SelectedIndex = listBoxSerialSentMicroscopeStage.Items.Count - 1;
            listBoxSerialSentMicroscopeStage.ScrollIntoView(listBoxSerialSentMicroscopeStage.SelectedItem);
            
            serialPortMicroscopeStage.WriteLine(command);
        }

        //Only allow 0 to 9 to be entered in text boxes
        private static bool IsTextAllowed(string text)
        {
            Regex regex = new Regex("[^0-9]"); //regex that matches disallowed text
            return !regex.IsMatch(text);
        }

        //Show Exception function from Basler Sample Code
        private void ShowException(Exception exception)
        {
            MessageBox.Show("Exception caught:\n" + exception.Message, "Error");
        }

        //Convert from bitmap to imagesource
        BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                BitmapImage bitmapimage = new BitmapImage();
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();

                return bitmapimage;
            }
        }
        //Convert BitmapImage to Bitmap PROBLEM WITH THIS NEED TO FIX
        private Bitmap BitmapImage2Bitmap(BitmapImage bitmapImage)
        {
            // BitmapImage bitmapImage = new BitmapImage(new Uri("../Images/test.png", UriKind.Relative));

            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage, null, null, null));  //added nulls
                enc.Save(outStream);
                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(outStream);

                return bitmap;
            }
        }

        //Get stage position conversion based on magnification of lens used. Pixel*stageConversion = stage position
        double[] getStageConversionFromObjective(string objective)
        {
            double[] stageConversion = new double[] { 1, 1 };
            switch (objective)
            {
                default:
                    Console.WriteLine("No Lens Selected");
                    break;
                case "4X Nikon":
                    stageConversion[0] = 29.2377;
                    stageConversion[1] = 30.1149;
                    break;
            }

            return stageConversion;
        }








        #endregion


    }
}
