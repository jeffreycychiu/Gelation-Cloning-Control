﻿using System;
using System.Collections.Generic;
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
using MahApps.Metro.Controls;



namespace Gelation_Cloning_Control
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        SerialPort serialPortArroyo = new SerialPort();

        static int CURRENTLIMIT = 6000;

        public MainWindow()
        {
            InitializeComponent();
            setSerialPortArroyo();
            
        }

        //Fill the combo box with the names of the avaliable serial ports
        private void cmbBoxSerialPort_DropDownOpened(object sender, EventArgs e)
        {
            string[] ports = SerialPort.GetPortNames();
            cmbBoxSerialPort.ItemsSource = SerialPort.GetPortNames();
            Console.WriteLine("font family: " + toggleLaser.FontFamily.ToString());
            Console.WriteLine("font size:  " + toggleLaser.FontSize.ToString());
            Console.WriteLine("font style:  " + toggleLaser.FontStyle.ToString());
            Console.WriteLine("font header font family: " + toggleLaser.HeaderFontFamily.ToString());
            Console.WriteLine("font stretch:  " + toggleLaser.FontStretch.ToString());
            Console.WriteLine("font weight:  " + toggleLaser.FontWeight.ToString());
        }

        //Connect to the serial port selected in the combo box
        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (btnConnect.Content.ToString() == "Connect")
            {
                try
                {
                    serialPortArroyo.PortName = cmbBoxSerialPort.Text;
                    serialPortArroyo.Open();
                    btnConnect.Content = "Disconnect";
                    cmbBoxSerialPort.IsEnabled = false;

                    //Attempt to send/recieve message - get the identification number of the driver
                    //serialPortArroyo.Write("*IDN?\n");
                    serialPortArroyoSend("*IDN?");

                    //Enable buttons & inputs
                    toggleLaser.IsEnabled = true;
                    textBoxCurrentSet.IsEnabled = true;
                    textBoxSerialSendCommand.IsEnabled = true;
                    btnSerialSendCommand.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex);
                }
            }
            else if (btnConnect.Content.ToString() == "Disconnect")
            {
                try
                {
                    serialPortArroyo.Close();
                    btnConnect.Content = "Connect";
                    cmbBoxSerialPort.IsEnabled = true;

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

        private void serialPortArroyoSend(string command)
        {
            listBoxSerialSent.Items.Add(command);
            listBoxSerialSent.SelectedIndex = listBoxSerialSent.Items.Count - 1;
            listBoxSerialSent.ScrollIntoView(listBoxSerialSent.SelectedItem);

            Console.WriteLine(listBoxSerialSent.Items.Count - 1);

            serialPortArroyo.Write(command + "\n"); //Requires carriage return to send c
        
        }

        //Turn the laser on in continuous wave (CW). Return the state of the laser (ON/OFF) after the button is toggled
        private void toggleLaser_Click(object sender, RoutedEventArgs e)
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
                textBoxCurrentSet.Text = 0.ToString() ;
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

        //TODO: Use the IsTextAllowed to make sure that only an integer value from 0-5000ish is allowed
        private void textBoxCurrent_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        //Helper function - only allow 0 to 9 to be entered
        private static bool IsTextAllowed(string text)
        {
            Regex regex = new Regex("[^0-9]"); //regex that matches disallowed text
            return !regex.IsMatch(text);
        }

        private void btnSerialSendCommand_Click(object sender, RoutedEventArgs e)
        {
            serialPortArroyoSend(textBoxSerialSendCommand.Text);
        }
    }

}

