using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace EOL_Net_Verifier
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource _scanCts;
        public Form1()
        {
            InitializeComponent();
        }

       

        private void Form1_Load(object sender, EventArgs e)
        {
            LoadAdapterList();
            // ensure results ListView sorts alive hosts first
            if (lstResults != null)
            {
                lstResults.ListViewItemSorter = new ResultListViewSorter();
            }
        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void lstAdapters_SelectedIndexChanged(object sender, EventArgs e)
        {
            
        }

        private void lstAdapters_ItemActivate(object sender, EventArgs e)
        {
            // Console.WriteLine("sdf");
            // MessageBox.Show("Hey");
            if (lstAdapters.SelectedItems.Count > 0)
            {
                ListViewItem item = lstAdapters.SelectedItems[0];
                var ip = (item.Tag as string) ?? string.Empty;
                //lblSelectedAdapter.Text = "Selected: " + ip;
                lblSelectedNetworkAdapter.Text = ip;
            }
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            // refresh the list of adapters
            this.RefreshAdapterList();
        }

        private void RefreshAdapterList() {
            lstAdapters.Items.Clear();
            GetAdapterDetails();


        }

        private void GetAdapterDetails() {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var ipProps = ni.GetIPProperties();
                    var unicast = ipProps.UnicastAddresses
                        .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork);

                    var ips = unicast.Select(u => u.Address.ToString());
                    var masks = unicast.Select(u => u.IPv4Mask != null ? u.IPv4Mask.ToString() : string.Empty);
                    var device = unicast.Select(u => u.PrefixOrigin.ToString());
                    
                    string ipList = string.Join(", ", ips);
                    string maskList = string.Join(", ", masks);

                    var macBytes = ni.GetPhysicalAddress().GetAddressBytes();
                    string mac = macBytes.Length == 0 ? string.Empty : BitConverter.ToString(macBytes).Replace("-", ":");

                    var firstIp = ips.FirstOrDefault() ?? string.Empty;

                    var item = new ListViewItem(new[] {
                        ni.Name,
                        ni.Description,
                        ni.OperationalStatus.ToString(),
                        mac,
                        maskList,
                        ipList
                    });

                    item.Tag = firstIp;

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

        private void LoadAdapterList()
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
            lstAdapters.Columns.Add("Subnet Mask", 120);
            lstAdapters.Columns.Add("IP Addresses", 200);

            lstAdapters.Items.Clear();

            GetAdapterDetails();

        }

        private async void btnScan_Click(object sender, EventArgs e)
        {
            if (_scanCts != null)
            {
                MessageBox.Show("Scan already running");
                return;
            }

            if (!int.TryParse(txtStart.Text, out int start) || !int.TryParse(txtEnd.Text, out int end))
            {
                MessageBox.Show("Invalid start/end values");
                return;
            }

            var selectedIp = lblSelectedNetworkAdapter.Text;
            if (string.IsNullOrWhiteSpace(selectedIp) || selectedIp == "XXXXX")
            {
                MessageBox.Show("Select an adapter first");
                return;
            }

            int lastDot = selectedIp.LastIndexOf('.');
            if (lastDot < 0)
            {
                MessageBox.Show("Adapter IP invalid");
                return;
            }

            string prefix = selectedIp.Substring(0, lastDot + 1);

            lstResults.View = View.Details;
            lstResults.FullRowSelect = true;
            lstResults.GridLines = true;
            lstResults.Columns.Clear();
            lstResults.Columns.Add("Address", 120);
            lstResults.Columns.Add("Hostname", 200);
            lstResults.Columns.Add("Alive", 60);
            lstResults.Columns.Add("RTT ms", 80);
            lstResults.Items.Clear();

            _scanCts = new CancellationTokenSource();
            btnScan.Enabled = false;

            var scanner = new IpScanner();

            try
            {
                await scanner.ScanRangeAsync(prefix, start, end,
                    onResult: res =>
                    {
                        this.BeginInvoke((Action)(() =>
                        {
                            var item = new ListViewItem(new[] { res.Address, res.Hostname, res.IsAlive ? "Yes" : "No", res.RoundtripTime.ToString() });
                            lstResults.Items.Add(item);
                            lstResults.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
                            lstResults.AutoResizeColumn(1, ColumnHeaderAutoResizeStyle.ColumnContent);
                            lstResults.AutoResizeColumn(2, ColumnHeaderAutoResizeStyle.ColumnContent);
                            lstResults.AutoResizeColumn(3, ColumnHeaderAutoResizeStyle.ColumnContent);
                            // keep list sorted so alive hosts appear first
                            lstResults.Sort();
                        }));
                    },
                    timeout: 1000,
                    maxConcurrent: 50,
                    cancellationToken: _scanCts.Token);
            }
            catch (OperationCanceledException)
            {
                // cancelled
            }
            finally
            {
                _scanCts = null;
                btnScan.Enabled = true;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (_scanCts != null)
            {
                _scanCts.Cancel();
            }
        }

        private class ResultListViewSorter : System.Collections.IComparer
        {
            public int Compare(object x, object y)
            {
                var ix = x as ListViewItem;
                var iy = y as ListViewItem;

                if (ix == null || iy == null) return 0;

                // Alive column is index 2: "Yes" or "No"
                bool ax = string.Equals(ix.SubItems[2].Text, "Yes", StringComparison.OrdinalIgnoreCase);
                bool ay = string.Equals(iy.SubItems[2].Text, "Yes", StringComparison.OrdinalIgnoreCase);

                // Alive first
                if (ax && !ay) return -1;
                if (!ax && ay) return 1;

                // otherwise sort by address
                return string.Compare(ix.SubItems[0].Text, iy.SubItems[0].Text, StringComparison.Ordinal);
            }
        }
    }
}
