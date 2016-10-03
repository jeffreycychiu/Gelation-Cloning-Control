using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
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
            string query = "*IDN?";
            serialPortArroyo.Write(query);


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
            Console.WriteLine("Data Received:");
            Console.Write(indata);
        }
    }

}

