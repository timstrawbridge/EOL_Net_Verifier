using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EOL_Net_Verifier
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // configure list view for details and columns
            lstAdapters.View = View.Details;
            lstAdapters.FullRowSelect = true;
            lstAdapters.GridLines = true;
            lstAdapters.Columns.Clear();
            lstAdapters.Columns.Add("Name", 120);
            lstAdapters.Columns.Add("Description", 220);
            lstAdapters.Columns.Add("Status", 90);
            lstAdapters.Columns.Add("MAC", 140);
            lstAdapters.Columns.Add("IP Addresses", 200);

            lstAdapters.Items.Clear();

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var ipProps = ni.GetIPProperties();
                    var ips = ipProps.UnicastAddresses
                        .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(u => u.Address.ToString());

                    string ipList = string.Join(", ", ips);

                    var macBytes = ni.GetPhysicalAddress().GetAddressBytes();
                    string mac = macBytes.Length == 0 ? string.Empty : BitConverter.ToString(macBytes).Replace("-", ":");

                    var item = new ListViewItem(new[] {
                        ni.Name,
                        ni.Description,
                        ni.OperationalStatus.ToString(),
                        mac,
                        ipList
                    });

                    lstAdapters.Items.Add(item);
                }

                // auto-size columns to content
                for (int i = 0; i < lstAdapters.Columns.Count; i++)
                {
                    lstAdapters.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to enumerate network adapters: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
