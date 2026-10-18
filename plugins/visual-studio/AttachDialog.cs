using System;
using System.Drawing;
using System.Windows.Forms;

namespace DolphinDotNet.VisualStudio;

internal sealed class AttachDialog : Form
{
    private readonly TextBox build = new TextBox(), host = new TextBox(), serial = new TextBox(), mappings = new TextBox(), heartbeat = new TextBox();
    private readonly NumericUpDown port = Number(1,65535), baud = Number(1,2000000), timeout = Number(1,120), heartbeatPort = Number(0,65535);
    private readonly ComboBox transport = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly string adapter;
    public AttachOptions Options { get; private set; }
    public AttachDialog(AttachOptions options, string adapter)
    {
        Options = options;this.adapter = adapter;
        Text = "Attach to DolphinDotNet";StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640,620);Size = new Size(720,680);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Controls.Add(layout);
        var folder = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true };
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,85));
        var browse = new Button { Text = "Browse…", Dock = DockStyle.Fill };
        browse.Click += (_, __) => { using (var picker = new FolderBrowserDialog()) { picker.SelectedPath = build.Text;if (picker.ShowDialog(this)==DialogResult.OK) build.Text=picker.SelectedPath; } };
        folder.Controls.Add(build,0,0);folder.Controls.Add(browse,1,0);
        Add(layout,"Debug build folder",folder);
        transport.Items.AddRange(new object[] { "BBA / TCP", "USB Gecko" });transport.SelectedIndex = options.Transport=="usbGecko" ? 1 : 0;
        Add(layout,"Transport",transport);Add(layout,"Host",host);Add(layout,"TCP port",port);
        Add(layout,"Serial port",serial);Add(layout,"Driver baud rate",baud);Add(layout,"Timeout (seconds)",timeout);
        Add(layout,"Live status IPv4 (optional)",heartbeat);Add(layout,"Live status UDP port",heartbeatPort);
        mappings.Multiline = true;mappings.ScrollBars = ScrollBars.Both;mappings.Height = 100;
        Add(layout,"Source mappings (JSON)",mappings);
        var help = new Label { Text = "Start a matching Debug DOL with Dolphin's GDB stub waiting before attaching. Source mappings map build prefixes to local source prefixes. USB Gecko's EXI slot is selected when building the DOL.", AutoSize = true, MaximumSize = new Size(500,0) };
        Add(layout,"",help);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var attach = new Button { Text = "Attach", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        attach.Click += (_, __) => Save();buttons.Controls.Add(attach);buttons.Controls.Add(cancel);Add(layout,"",buttons);
        AcceptButton = attach;CancelButton = cancel;
        build.Text=options.BuildFolder;host.Text=options.Host;port.Value=Bound(options.Port,port);
        serial.Text=options.SerialPort;baud.Value=Bound(options.BaudRate,baud);timeout.Value=Bound(options.TimeoutSeconds,timeout);
        mappings.Text=options.SourceFileMapJson;heartbeat.Text=options.HeartbeatHost;heartbeatPort.Value=Bound(options.HeartbeatPort,heartbeatPort);
        transport.SelectedIndexChanged += (_, __) => UpdateTransport();UpdateTransport();
    }
    private void Save()
    {
        try
        {
            var value = new AttachOptions { BuildFolder=build.Text.Trim(), Transport=transport.SelectedIndex==0 ? "tcp" : "usbGecko",
                Host=host.Text.Trim(),Port=(int)port.Value,SerialPort=serial.Text.Trim(),BaudRate=(int)baud.Value,TimeoutSeconds=(int)timeout.Value,
                SourceFileMapJson=mappings.Text,HeartbeatHost=heartbeat.Text.Trim(),HeartbeatPort=(int)heartbeatPort.Value };
            value.CreateLaunch(adapter);Options=value;DialogResult=DialogResult.OK;Close();
        }
        catch (Exception error) { MessageBox.Show(this,error.Message,"Check attach settings",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
    private void UpdateTransport() { host.Enabled=port.Enabled=transport.SelectedIndex==0;serial.Enabled=baud.Enabled=transport.SelectedIndex==1; }
    private static NumericUpDown Number(int min,int max) => new NumericUpDown { Minimum=min,Maximum=max };
    private static decimal Bound(int value,NumericUpDown control) => Math.Max(control.Minimum,Math.Min(control.Maximum,value));
    private static void Add(TableLayoutPanel layout,string label,Control control)
    {
        var row=layout.RowCount++;layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text=label,AutoSize=true,Margin=new Padding(0,7,8,7) },0,row);
        control.Dock=DockStyle.Fill;control.Margin=new Padding(0,3,0,3);layout.Controls.Add(control,1,row);
    }
}
