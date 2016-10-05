using System;
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
        //Thread readThread = new Thread();

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
                    serialPortArroyo.Write("*IDN?\n");

                    //Enable buttons
                    toggleLaser.IsEnabled = true;
                    textBoxCurrent.IsEnabled = true;
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

                    //Disable
                    toggleLaser.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex);
                }
            }
        }

        //Test button for reading the things on serial port
        private void btnnReadValue_Click(object sender, RoutedEventArgs e)
        {
            serialPortArroyo.Write("*IDN?\n");
            //Console.WriteLine("Data Recieved: " + serialPortArroyo.ReadLine());

        }

        //Set the serial port object to the Arroyo Driver fixed defaults
        public void setSerialPortArroyo()
        {
            serialPortArroyo.BaudRate = 38400;
            serialPortArroyo.Parity = Parity.None;
            serialPortArroyo.DataBits = 8;
            serialPortArroyo.StopBits = StopBits.One;
            serialPortArroyo.Handshake = Handshake.None; //Flow control = none

            serialPortArroyo.DataReceived += SerialPortArroyo_DataReceived;

        }

        private void SerialPortArroyo_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string indata = serialPortArroyo.ReadExisting();
            this.Dispatcher.Invoke(() =>
            {
                listBoxSerialPort.Items.Add(indata);
            });
            
            Console.WriteLine("Data Received:");
            Console.Write(indata);
        }

        //Turn the laser on in continuous wave (CW)
        private void toggleLaser_Click(object sender, RoutedEventArgs e)
        {
            if (toggleLaser.IsChecked == true)
            {
                serialPortArroyo.Write("LASer:OUTput 1\n");
                serialPortArroyo.Write("LASer:OUTput?\n");
            }
            else
            {
                serialPortArroyo.Write("LASer:OUTput 0\n");
                serialPortArroyo.Write("LASer:OUTput?\n");
            }
        }

        private void btnSetCurrent_Click(object sender, RoutedEventArgs e)
        {

            btnSetCurrent.IsEnabled = false;
        }

        //Enable the Set Current Button (btnSetCurrent) when the current is changed
        private void textBoxCurrent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (serialPortArroyo.IsOpen == true)
                btnSetCurrent.IsEnabled = true;
            else
                btnSetCurrent.IsEnabled = false;
        }

        //TODO: Use the IsTextAllowed to make sure that only an integer value from 0-5000ish is allowed
        private void textBoxCurrent_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Console.WriteLine(e);

        }

        private static bool IsTextAllowed(string text)
        {
            Regex regex = new Regex("[^0-9.-]+"); //regex that matches disallowed text
            return !regex.IsMatch(text);
        }
    }

}

