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
        IFloatParameter gain = null;

        DispatcherTimer timerUpdateBaslerDeviceList = new DispatcherTimer();
        DispatcherTimer timerUpdateStagePosition = new DispatcherTimer();

        static int CURRENTLIMIT = 6000; //Max current for the laser in milliamps
        static int PERIODLIMIT = 10000; //Max period in milliseconds

        public int zeroX = 0;
        public int zeroY = 0;
        public int zeroZ = 0;

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
            serialPortMicroscopeStageSend("GR," + textBoxXGoTo.Text + "," + textBoxYGoto.Text + "," + textBoxZGoTo.Text);
        }

        //Select folder for the images to be saved during a scanning
        private void btnSaveScanImageFolderPath_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "bmp (*.bmp)|*.bmp";
            if (saveFileDialog.ShowDialog() == true)
            {
                textBoxSaveScanImageFolderPath.Text = saveFileDialog.FileName;
                //(windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image.Save(saveFileDialog.FileName, ImageFormat.Bmp);
            }
        }

        //Stage scanning function
        private void btnScan_Click(object sender, RoutedEventArgs e)
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
                case "4X Nikon":
                    moveStageX = -60000;
                    moveStageY = -60000;
                    break;
                case "10X Nikon":
                    moveStageX = -24000;
                    moveStageY = -24000;
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

            serialPortMicroscopeStageSend("MACRO");
            for (int row = 0; row < yFields; row++)
            {
                for (int column = 0; column < xFields; column++)
                {
                    serialPortMicroscopeStageSend("GR," + moveStageX.ToString() + ", 0");
                    serialPortMicroscopeStageSend("WAIT 1000");
                    //if (checkBoxSaveScanImages.IsChecked == true)
                    //{
                    //    string[] filePath = new string[2];
                    //    filePath = textBoxSaveScanImageFolderPath.Text.Split(',');
                    //    (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image.Save(filePath[0] + "-" + row.ToString() + "-" + column.ToString(), ImageFormat.Bmp);
                    //}
                    
                }
                if (row < yFields - 1)
                {
                    moveStageX = -moveStageX;
                    serialPortMicroscopeStageSend("GR, 0, " + moveStageY.ToString());
                    serialPortMicroscopeStageSend("WAIT 1000");
                }
            }
            serialPortMicroscopeStageSend("MACRO");
            

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
            //btnSetPeriod.IsEnabled = true;
            //btnSetDutyCycle.IsEnabled = true;
            btnFireCycleOnce.IsEnabled = true;

            //Create a stopwatch event


        }

        private void radioBtnPWM_Unchecked(object sender, RoutedEventArgs e)
        {
            radioBtnCW.IsEnabled = true;
            textBoxPeriodSet.IsEnabled = false;
            textBoxDutyCycleSet.IsEnabled = false;
            btnSetPeriod.IsEnabled = false;
            btnSetDutyCycle.IsEnabled = false;
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
        }

        //Fire one period of the cycle set by the duty cycle/period settings
        private void btnFireCycleOnce_Click(object sender, RoutedEventArgs e)
        {
            //Create a new timer for this purpose:
            int period = int.Parse(textBoxPeriodSet.Text);
            double dutyCycle = double.Parse(textBoxDutyCycleSet.Text);



            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            serialPortArroyoSend("LASer:OUTput 1");
            serialPortArroyoSend("LASer:OUTput?");

            while (stopwatch.ElapsedMilliseconds < Math.Floor(period * dutyCycle / 100)) ;  //do nothing

            serialPortArroyoSend("LASer:OUTput 0");
            serialPortArroyoSend("LASer:OUTput?");

            while (stopwatch.ElapsedMilliseconds < period) ; //do nothing until period over

            stopwatch.Reset();

        }



        private void textBoxPeriodSet_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnSetPeriod.IsEnabled = true;
        }

        private void textBoxDutyCycleSet_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnSetDutyCycle.IsEnabled = true;
        }

        private void textBoxPeriodSet_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private void textBoxDutyCycleSet_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
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

                    //if (camera.Parameters.Contains(PLCamera.GainAbs))
                    //{
                    //    gain = camera.Parameters[PLCamera.GainAbs];
                    //}
                    //else
                    //{
                    //    gain = camera.Parameters[PLCamera.Gain];
                    //}

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
                    //textBoxGain.Text = gain.GetValue().ToString();
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

                        //BitmapImage bitmapImageOld = (BitmapImage)imageDisplay.Source;


                        // Provide the display control with the new bitmap. This action automatically updates the display.
                        //pictureBox.Image = bitmap;
                        (windowsFormsHost.Child as System.Windows.Forms.PictureBox).Image = bitmap;
                        //imageDisplay.Source = BitmapToBitmapImage(bitmap);

                        
                        //if (bitmapImageOld != null)
                        if(bitmapOld != null)
                        {
                            // Dispose the bitmap.
                            //Bitmap bitmapOld = BitmapImage2Bitmap(bitmapImageOld);
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
            //textBoxGain.IsEnabled = canStop;
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
                        //ListViewItem item = new ListViewItem(cameraInfo[CameraInfoKey.FriendlyName]);
                        ListViewItem item = new ListViewItem();

                        // Create the tool tip text.
                        string toolTipText = "";
                        foreach (KeyValuePair<string, string> kvp in cameraInfo)
                        {
                            toolTipText += kvp.Key + ": " + kvp.Value + "\n";
                        }
                        item.ToolTip = toolTipText;

                        // Store the camera info in the displayed item.
                        item.Tag = cameraInfo;

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
                gain.ParseAndSetValue(textBoxGain.Text);
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
            foreach(var positionString in recievedData.Split(','))
            {
                if (int.TryParse(positionString, out positions[numValues]) == true)
                    numValues++;
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

        #endregion


    }
}
