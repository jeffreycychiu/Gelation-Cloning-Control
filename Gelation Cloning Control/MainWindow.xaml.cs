using System;
using System.Diagnostics;
using System.Drawing;
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
//using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using PylonC.NET;
using Basler.Pylon;
using PylonC.NETSupportLibrary;


namespace Gelation_Cloning_Control
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        SerialPort serialPortArroyo = new SerialPort();
        SerialPort serialPortMicroscopeStage = new SerialPort();

        //ImageProvider is for the Basler Camera class
        private ImageProvider imageProvider = new ImageProvider();
        private Bitmap m_bitmap = null; /* The bitmap is used for displaying the image. */

        DispatcherTimer UpdateBaslerDeviceListTimer = new DispatcherTimer();

        static int CURRENTLIMIT = 6000; //Max current for the laser in milliamps
        static int PERIODLIMIT = 10000; //Max period in milliseconds

        public MainWindow()
        {
            InitializeComponent();
            setSerialPortArroyo();
            setSerialPortMicroscopeStage();
#if DEBUG
            /* This is a special debug setting needed only for GigE cameras.
                See 'Building Applications with pylon' in the Programmer's Guide. */
            Environment.SetEnvironmentVariable("PYLON_GIGE_HEARTBEAT", "300000" /*ms*/);
#endif
            PylonC.NET.Pylon.Initialize();

            //PylonC.NET.Pylon.Terminate();
            /* Register for the events of the image provider needed for proper operation. */
            imageProvider.GrabErrorEvent += new ImageProvider.GrabErrorEventHandler(OnGrabErrorEventCallback);
            imageProvider.DeviceRemovedEvent += new ImageProvider.DeviceRemovedEventHandler(OnDeviceRemovedEventCallback);
            imageProvider.DeviceOpenedEvent += new ImageProvider.DeviceOpenedEventHandler(OnDeviceOpenedEventCallback);
            imageProvider.DeviceClosedEvent += new ImageProvider.DeviceClosedEventHandler(OnDeviceClosedEventCallback);
            imageProvider.GrabbingStartedEvent += new ImageProvider.GrabbingStartedEventHandler(OnGrabbingStartedEventCallback);
            imageProvider.ImageReadyEvent += new ImageProvider.ImageReadyEventHandler(OnImageReadyEventCallback);
            imageProvider.GrabbingStoppedEvent += new ImageProvider.GrabbingStoppedEventHandler(OnGrabbingStoppedEventCallback);

            //UpdateBaslerDeviceListTimer.Tick += new EventHandler(updateBaslerDeviceListTimer_Tick);
            //UpdateBaslerDeviceListTimer.Interval = new TimeSpan(0, 0, 0, 0, 500);//Not sure how fas this has to be yet
            //UpdateBaslerDeviceListTimer.IsEnabled = true;

            UpdateBaslerDeviceList();
            /* Enable the tool strip buttons according to the state of the image provider. */
            EnableButtons(imageProvider.IsOpen, false);
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
                    cmbBoxSerialPortMicroscopeStage.IsEnabled = false;

                    //Attempt to send/recieve message - this command recieves the peripherals connected to the controller
                    //serialPortMicroscopeStageSend("*IDN?");

                    //TODO: Fix this - not getting response right now
                    serialPortMicroscopeStage.Write("?");


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
                    textBoxSerialSendCommand.IsEnabled = true;
                    btnSerialSendCommand.IsEnabled = true;

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
                    textBoxSerialSendCommand.IsEnabled = false;
                    btnSerialSendCommand.IsEnabled = false;
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
            /* Close the currently open image provider. */
            /* Stops the grabbing of images. */
            Stop();
            /* Close the image provider. */
            CloseTheImageProvider();

            /* Open the selected image provider. */
            if (listViewCamera.SelectedItems.Count > 0)
            {
                /* Get the first selected item. */
                //ListViewItem item = listViewCamera.SelectedItems[0];
                ListViewItem item = (ListViewItem)listViewCamera.SelectedItem;
                /* Get the attached device data. */
                DeviceEnumerator.Device device = item.Tag as DeviceEnumerator.Device;
                try
                {
                    /* Open the image provider using the index from the device data. */
                    imageProvider.Open(device.Index);
                    //imageProvider.Open(0);
                }
                catch (Exception exception)
                {
                    ShowException(exception, imageProvider.GetLastErrorMessage());
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

        /* Stops the image provider and handles exceptions. */
        private void Stop()
        {
            /* Stop the grabbing. */
            try
            {
                imageProvider.Stop();
            }
            catch (Exception e)
            {
                ShowException(e, imageProvider.GetLastErrorMessage());
            }
        }

        /* Closes the image provider and handles exceptions. */
        private void CloseTheImageProvider()
        {
            /* Close the image provider. */
            try
            {
                imageProvider.Close();
            }
            catch (Exception e)
            {
                ShowException(e, imageProvider.GetLastErrorMessage());
            }
        }

        /* Starts the grabbing of one image and handles exceptions. */
        private void OneShot()
        {
            try
            {
                imageProvider.OneShot(); /* Starts the grabbing of one image. */
            }
            catch (Exception e)
            {
                ShowException(e, imageProvider.GetLastErrorMessage());
            }
        }

        /* Starts the grabbing of images until the grabbing is stopped and handles exceptions. */
        private void ContinuousShot()
        {
            try
            {
                imageProvider.ContinuousShot(); /* Start the grabbing of images until grabbing is stopped. */
            }
            catch (Exception e)
            {
                ShowException(e, imageProvider.GetLastErrorMessage());
            }
        }

        /* Handles the event related to the occurrence of an error while grabbing proceeds. */
        private void OnGrabErrorEventCallback(Exception grabException, string additionalErrorMessage)
        {
            if (!Dispatcher.CheckAccess())
            {
                /* If called from a different thread, we must use the Invoke method to marshal the call to the proper thread. */
                Dispatcher.BeginInvoke(new ImageProvider.GrabErrorEventHandler(OnGrabErrorEventCallback), grabException, additionalErrorMessage);
                return;
            }
            ShowException(grabException, additionalErrorMessage);
        }

        /* Handles the event related to the removal of a currently open device. */
        private void OnDeviceRemovedEventCallback()
        {
            if (!Dispatcher.CheckAccess())
            {
                /* If called from a different thread, we must use the Invoke method to marshal the call to the proper thread. */
                Dispatcher.BeginInvoke(new ImageProvider.DeviceRemovedEventHandler(OnDeviceRemovedEventCallback));
                return;
            }
            /* Disable the buttons. */
            EnableButtons(false, false);
            /* Stops the grabbing of images. */
            Stop();
            /* Close the image provider. */
            CloseTheImageProvider();
            /* Since one device is gone, the list needs to be updated. */
            UpdateBaslerDeviceList();

        }

        /* Handles the event related to a device being open. */
        private void OnDeviceOpenedEventCallback()
        {
            if (!Dispatcher.CheckAccess())
            {
                /* If called from a different thread, we must use the Invoke method to marshal the call to the proper thread. */
                Dispatcher.BeginInvoke(new ImageProvider.DeviceOpenedEventHandler(OnDeviceOpenedEventCallback));
                return;
            }
            /* The image provider is ready to grab. Enable the grab buttons. */
            EnableButtons(true, false);
        }

        /* Handles the event related to a device being closed. */
        private void OnDeviceClosedEventCallback()
        {
            if (!Dispatcher.CheckAccess())
            {
                /* If called from a different thread, we must use the Invoke method to marshal the call to the proper thread. */
                Dispatcher.BeginInvoke(new ImageProvider.DeviceClosedEventHandler(OnDeviceClosedEventCallback));
                return;
            }
            /* The image provider is closed. Disable all buttons. */
            EnableButtons(false, false);
        }

        /* Handles the event related to the image provider executing grabbing. */
        private void OnGrabbingStartedEventCallback()
        {
            if (!Dispatcher.CheckAccess())
            {
                /* If called from a different thread, we must use the Invoke method to marshal the call to the proper thread. */
                Dispatcher.BeginInvoke(new ImageProvider.GrabbingStartedEventHandler(OnGrabbingStartedEventCallback));
                return;
            }

            /* Do not update device list while grabbing to avoid jitter because the GUI-Thread is blocked for a short time when enumerating. */
            UpdateBaslerDeviceListTimer.Stop();

            /* The image provider is grabbing. Disable the grab buttons. Enable the stop button. */
            EnableButtons(false, true);
        }

        /* Handles the event related to an image having been taken and waiting for processing. */
        private void OnImageReadyEventCallback()
        {
            if (!Dispatcher.CheckAccess())
            {
                /* If called from a different thread, we must use the Invoke method to marshal the call to the proper thread. */
                Dispatcher.BeginInvoke(new ImageProvider.ImageReadyEventHandler(OnImageReadyEventCallback));
                return;
            }

            try
            {
                /* Acquire the image from the image provider. Only show the latest image. The camera may acquire images faster than images can be displayed*/
                ImageProvider.Image image = imageProvider.GetLatestImage();

                /* Check if the image has been removed in the meantime. */
                if (image != null)
                {
                    /* Check if the image is compatible with the currently used bitmap. */
                    if (BitmapFactory.IsCompatible(m_bitmap, image.Width, image.Height, image.Color))
                    {
                        /* Update the bitmap with the image data. */
                        BitmapFactory.UpdateBitmap(m_bitmap, image.Buffer, image.Width, image.Height, image.Color);
                        /* To show the new image, request the display control to update itself. */
                        imageDisplay.Source = BitmapToBitmapImage(m_bitmap);
                        //pictureBox.Refresh();
                    }
                    else /* A new bitmap is required. */
                    {
                        BitmapFactory.CreateBitmap(out m_bitmap, image.Width, image.Height, image.Color);
                        BitmapFactory.UpdateBitmap(m_bitmap, image.Buffer, image.Width, image.Height, image.Color);
                        /* We have to dispose the bitmap after assigning the new one to the display control. */
                        //Bitmap bitmap = pictureBox.Image as Bitmap;
                        //Bitmap bitmap = imageDisplay.Source;

                        /* Provide the display control with the new bitmap. This action automatically updates the display. */

                        imageDisplay.Source = BitmapToBitmapImage(m_bitmap);
                         
                        //if (bitmap != null)
                        //{
                        //    /* Dispose the bitmap. */
                        //    bitmap.Dispose();
                        //}
                    
                    }
                    /* The processing of the image is done. Release the image buffer. */
                    imageProvider.ReleaseImage();
                    /* The buffer can be used for the next image grabs. */
                }
            }
            catch (Exception e)
            {
                ShowException(e, imageProvider.GetLastErrorMessage());
            }
        }

        /* Handles the event related to the image provider having stopped grabbing. */
        private void OnGrabbingStoppedEventCallback()
        {
            if (!Dispatcher.CheckAccess())
            {
                /* If called from a different thread, we must use the Invoke method to marshal the call to the proper thread. */
                Dispatcher.BeginInvoke(new ImageProvider.GrabbingStoppedEventHandler(OnGrabbingStoppedEventCallback));
                return;
            }

            /* Enable device list update again */
            UpdateBaslerDeviceListTimer.Start();

            /* The image provider stopped grabbing. Enable the grab buttons. Disable the stop button. */
            EnableButtons(imageProvider.IsOpen, false);
        }

        /* Helps to set the states of Camera buttons. */
        private void EnableButtons(bool canGrab, bool canStop)
        {
            btnCameraSingleShot.IsEnabled = canGrab;
            btnCameraContinuousShot.IsEnabled = canGrab;
            btnCameraStop.IsEnabled = canStop;
        }

        /* Updates the list of available devices in the upper left area. */
        private void UpdateBaslerDeviceList()
        {
            try
            {
                
                /* Ask the device enumerator for a list of devices. */
                List<DeviceEnumerator.Device> list = DeviceEnumerator.EnumerateDevices();
                
                ItemCollection items = listViewCamera.Items;
               
                /* Add each new device to the list. */
                foreach (DeviceEnumerator.Device device in list)
                {
                    bool newitem = true;
                    /* For each enumerated device check whether it is in the list view. */
                    foreach (ListViewItem item in items)
                    {
                        /* Retrieve the device data from the list view item. */
                        DeviceEnumerator.Device tag = item.Tag as DeviceEnumerator.Device;

                        if (tag.FullName == device.FullName)
                        {
                            /* Update the device index. The index is used for opening the camera. It may change when enumerating devices. */
                            tag.Index = device.Index;
                            /* No new item needs to be added to the list view */
                            newitem = false;
                            break;
                        }
                    }

                    /* If the device is not in the list view yet the add it to the list view. */
                    if (newitem)
                    {
                        //ListViewItem item = new ListViewItem(device.Name);
                        ListViewItem item = new ListViewItem();
                        if (device.Tooltip.Length > 0)
                        {
                            item.ToolTip = device.Tooltip;
                        }
                        item.Tag = device;
                        item.DataContext = device.Name;
                        /* Attach the device data. */
                       
                        listViewCamera.Items.Add(item);

                    }

                    
                }
                
                /* Delete old devices which are removed. */
                foreach (ListViewItem item in items)
                {
                    bool exists = false;

                    /* For each device in the list view check whether it has not been found by device enumeration. */
                    foreach (DeviceEnumerator.Device device in list)
                    {
                        if (((DeviceEnumerator.Device)item.Tag).FullName == device.FullName)
                        {
                            exists = true;
                            break;
                        }
                    }
                    /* If the device has not been found by enumeration then remove from the list view. */
                    if (!exists)
                    {
                        listViewCamera.Items.Remove(item);
                    }
                }
            }
            catch (Exception e)
            {
                ShowException(e, imageProvider.GetLastErrorMessage());
            }
        }

        //Updates the device list on each timer tick
        private void updateBaslerDeviceListTimer_Tick(object sender, EventArgs e)
        {
            UpdateBaslerDeviceList();
        }

        #endregion

        #region Serial Event Handlers
        //Write the data recieved from the Arroyo instrument to the listbox. Helpful for debugging
        private void SerialPortArroyo_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string recievedData = serialPortArroyo.ReadExisting();
            this.Dispatcher.Invoke(() =>
            {
                listBoxSerialRecieved.Items.Add(recievedData);
                listBoxSerialRecieved.SelectedIndex = listBoxSerialRecieved.Items.Count - 1;
                listBoxSerialRecieved.ScrollIntoView(listBoxSerialRecieved.Items);

                Console.WriteLine("sel index " + listBoxSerialRecieved.SelectedIndex);
                Console.WriteLine("sel item " + listBoxSerialRecieved.SelectedItem);
            });

            //Handle the recieved data
            switch (recievedData)
            {
                //case: ""
            }

            //Console.WriteLine("Data Received:");
            //Console.Write(recievedData);
        }

        //Write the data recieved from the Mircoscope stage to the listbox. Helpful for debugging
        private void SerialPortMicroscopeStage_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string recievedData = serialPortMicroscopeStage.ReadExisting();
            this.Dispatcher.Invoke(() =>
            {
                listBoxSerialRecievedMicroscopeStage.Items.Add(recievedData);
                listBoxSerialRecievedMicroscopeStage.SelectedIndex = listBoxSerialRecievedMicroscopeStage.Items.Count - 1;
                listBoxSerialRecievedMicroscopeStage.ScrollIntoView(listBoxSerialRecievedMicroscopeStage.Items);
            });

            Console.WriteLine("Data Received from Microscope Stage: " + recievedData);
        }

        private void btnSerialSendCommand_Click(object sender, RoutedEventArgs e)
        {
            serialPortArroyoSend(textBoxSerialSendCommand.Text);
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
            PylonC.NET.Pylon.Terminate();
        }
        #endregion

        #region Helper Functions (serial send, text checking, etc.)

        //Send serial port data to the Arroyo. Automatically appends an endline "\n" character
        private void serialPortArroyoSend(string command)
        {
            listBoxSerialSent.Items.Add(command);
            listBoxSerialSent.SelectedIndex = listBoxSerialSent.Items.Count - 1;
            listBoxSerialSent.ScrollIntoView(listBoxSerialSent.SelectedItem);

            Console.WriteLine(listBoxSerialSent.Items.Count - 1);

            serialPortArroyo.Write(command + "\n"); //Requires newline to send
        }

        private void serialPortMicroscopeStageSend(string command)
        {
            listBoxSerialSentMicroscopeStage.Items.Add(command);
            listBoxSerialSentMicroscopeStage.SelectedIndex = listBoxSerialSentMicroscopeStage.Items.Count - 1;
            listBoxSerialSentMicroscopeStage.ScrollIntoView(listBoxSerialSentMicroscopeStage.SelectedItem);

            Console.WriteLine(listBoxSerialSentMicroscopeStage.Items.Count - 1);

            serialPortMicroscopeStage.WriteLine(command);
        }

        //Only allow 0 to 9 to be entered in text boxes
        private static bool IsTextAllowed(string text)
        {
            Regex regex = new Regex("[^0-9]"); //regex that matches disallowed text
            return !regex.IsMatch(text);
        }

        //Show Exception function from Basler Sample Code
        private void ShowException(Exception e, string additionalErrorMessage)
        {
            string more = "\n\nLast error message (may not belong to the exception):\n" + additionalErrorMessage;
            MessageBox.Show("Exception caught:\n" + e.Message + (additionalErrorMessage.Length > 0 ? more : ""), "Error");
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



        #endregion


    }
}
