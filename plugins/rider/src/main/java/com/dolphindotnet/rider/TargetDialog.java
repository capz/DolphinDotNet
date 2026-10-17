package com.dolphindotnet.rider;

import com.google.gson.Gson;
import com.intellij.icons.AllIcons;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.ui.*;
import com.intellij.execution.process.CapturingProcessHandler;
import com.intellij.ui.*;
import com.intellij.ui.components.*;
import com.intellij.util.ui.JBUI;
import javax.swing.*;
import java.awt.*;
import java.util.*;

final class TargetDialog extends DialogWrapper {
    private final DefaultListModel<TargetProfile> model = new DefaultListModel<>();
    private final JBList<TargetProfile> list = new JBList<>(model);
    private final JBTextField name = new JBTextField();
    private final ComboBox<String> type = new ComboBox<>(new String[]{"BBA / TCP", "USB Gecko"});
    private final JBTextField host = new JBTextField();
    private final JSpinner port = new JSpinner(new SpinnerNumberModel(new TargetProfile().port, 1, 65535, 1));
    private final ComboBox<String> serial = new ComboBox<>();
    private final ComboBox<String> slot = new ComboBox<>(new String[]{"Slot A", "Slot B"});
    private final JSpinner baud = new JSpinner(new SpinnerNumberModel(115200, 1, Integer.MAX_VALUE, 1));
    private final JSpinner timeout = new JSpinner(new SpinnerNumberModel(10, 1, 120, 1));
    private final JPanel tcp = Forms.form(), usb = Forms.form(), editor = Forms.form(), usbAdvanced = Forms.form();
    private final JBLabel footer = new JBLabel();
    private TargetProfile editing;
    private boolean loading;
    private volatile boolean disposed;
    private String selectedId;

    TargetDialog(Project project, String selectedId) {
        super(project, true); this.selectedId = selectedId;
        setTitle("Manage targets"); setOKButtonText("Save");
        TargetStore.getInstance().targets().forEach(model::addElement);
        serial.setEditable(true);
        list.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
        list.addListSelectionListener(e -> { if (!e.getValueIsAdjusting()) select(); });
        type.addActionListener(e -> { if (!loading) { capture(); showTransport(); } });
        init();
        int index = 0;
        for (int i = 0; i < model.size(); i++) if (Objects.equals(model.get(i).id, selectedId)) index = i;
        if (!model.isEmpty()) list.setSelectedIndex(index); else showTransport();
    }
    @Override protected JComponent createCenterPanel() {
        var add = new JButton(AllIcons.General.Add); add.setToolTipText("Add target");
        var remove = new JButton(AllIcons.General.Remove); remove.setToolTipText("Remove target");
        add.addActionListener(e -> {
            capture(); var t = new TargetProfile();
            model.addElement(t); list.setSelectedValue(t, true); name.requestFocusInWindow(); name.selectAll();
        });
        remove.addActionListener(e -> {
            int i = list.getSelectedIndex(); if (i < 0) return;
            editing = null; model.remove(i);
            if (!model.isEmpty()) list.setSelectedIndex(Math.min(i, model.size() - 1)); else { select(); }
        });
        var toolbar = new JPanel(new FlowLayout(FlowLayout.LEFT, 0, 0)); toolbar.add(add); toolbar.add(remove);
        var left = new JPanel(new BorderLayout()); left.add(toolbar, BorderLayout.NORTH); left.add(new JBScrollPane(list));
        left.setPreferredSize(JBUI.size(220, 440));
        Forms.row(editor, 0, "Name:", name); Forms.row(editor, 1, "Connection type:", type);
        Forms.row(tcp, 0, "Host:", host); Forms.row(tcp, 1, "TCP port:", port);
        Forms.row(tcp, 2, "", new JBLabel("<html>Dolphin wait-for-Rider profile: 127.0.0.1:2166.<br>For consoles, use the address and port configured in the build.</html>"));
        var refresh = new JButton("Refresh"); refresh.addActionListener(e -> refreshPorts(refresh));
        Forms.row(usb, 0, "Serial port:", Forms.beside(serial, refresh));
        Forms.row(usb, 1, "", new JBLabel("Choose the USB Gecko serial device on this computer."));
        Forms.row(usb, 2, "Expected console slot:", slot);
        Forms.row(usb, 3, "", new JBLabel("<html>Must match the debug build. This setting does not<br>change the running console.</html>"));
        var transport = new JPanel(); transport.setLayout(new BoxLayout(transport, BoxLayout.Y_AXIS)); transport.add(tcp); transport.add(usb);
        Forms.row(editor, 2, "", transport);
        var advanced = new JPanel(new BorderLayout()); var details = Forms.form();
        Forms.row(details, 0, "Response timeout (seconds):", timeout);
        var baudPanel = Forms.beside(baud, new JBLabel("8N1 · no handshake"));
        Forms.row(usbAdvanced, 0, "Driver baud compatibility:", baudPanel);
        Forms.row(usbAdvanced, 1, "", new JBLabel("<html>USB FIFO speed is not a UART baud rate.<br>The compatibility value has not been hardware validated.</html>"));
        Forms.row(details, 1, "", usbAdvanced);
        var hideable = new HideableDecorator(advanced, "Advanced connection options", false);
        hideable.setContentComponent(details);
        // Baud controls are relevant only to USB; timeout is shared.
        type.addActionListener(e -> { baud.setEnabled(type.getSelectedIndex() == 1); });
        Forms.row(editor, 3, "", advanced); Forms.row(editor, 4, "", footer);
        var right = new JPanel(new BorderLayout()); right.setBorder(JBUI.Borders.empty(8, 20)); right.add(editor, BorderLayout.NORTH);
        var root = new JPanel(new BorderLayout()); root.add(left, BorderLayout.WEST); root.add(right);
        root.setPreferredSize(JBUI.size(850, 490)); return root;
    }
    private void select() {
        capture(); editing = list.getSelectedValue(); loading = true;
        if (editing != null) {
            name.setText(editing.name); type.setSelectedIndex(editing.transport.equals("usbGecko") ? 1 : 0);
            host.setText(editing.host); port.setValue(editing.port); serial.getEditor().setItem(editing.serialPort);
            slot.setSelectedIndex(editing.expectedSlot.equals("A") ? 0 : 1); baud.setValue(editing.baudRate); timeout.setValue(editing.timeoutSeconds);
        }
        loading = false; showTransport();
    }
    private void capture() {
        if (loading || editing == null) return;
        editing.name = name.getText().trim(); editing.transport = type.getSelectedIndex() == 1 ? "usbGecko" : "tcp";
        editing.host = host.getText().trim(); editing.port = (int)port.getValue();
        editing.serialPort = Objects.toString(serial.getEditor().getItem(), "").trim();
        editing.expectedSlot = slot.getSelectedIndex() == 0 ? "A" : "B";
        editing.baudRate = (int)baud.getValue(); editing.timeoutSeconds = (int)timeout.getValue(); list.repaint();
    }
    private void showTransport() {
        boolean isUsb = type.getSelectedIndex() == 1;
        tcp.setVisible(!isUsb); usb.setVisible(isUsb); usbAdvanced.setVisible(isUsb); baud.setEnabled(isUsb);
        editor.setVisible(editing != null);
        footer.setText("The target must already be running a " + (isUsb ? "USB Gecko" : "TCP") + " debug build.");
        if (editor.getParent() != null) editor.getParent().revalidate();
    }
    private void refreshPorts(JButton refresh) {
        refresh.setEnabled(false);
        ApplicationManager.getApplication().executeOnPooledThread(() -> {
            String[] ports = null; String error = null;
            try {
                var result = new CapturingProcessHandler(AdapterCommand.create("--list-serial-ports")).runProcess(15000);
                if (result.isTimeout() || result.getExitCode() != 0) throw new IllegalStateException("Could not enumerate serial ports. " + result.getStderr());
                ports = new Gson().fromJson(result.getStdout(), String[].class);
                if (ports == null) throw new IllegalStateException("Invalid serial-port list from adapter.");
            } catch (Exception e) { error = e.getMessage(); }
            var found = ports; var failure = error;
            ApplicationManager.getApplication().invokeLater(() -> {
                if (disposed) return;
                refresh.setEnabled(true);
                if (failure != null) { Messages.showErrorDialog(getContentPane(), failure, "Serial ports"); return; }
                var value = serial.getEditor().getItem(); serial.removeAllItems();
                for (var p : found) serial.addItem(p);
                serial.getEditor().setItem(value); // Preserve manually entered Linux/macOS paths.
            });
        });
    }
    @Override protected void doOKAction() {
        try {
            port.commitEdit(); baud.commitEdit(); timeout.commitEdit(); capture();
            var targets = Collections.list(model.elements());
            for (var t : targets) { try { t.validate(); } catch (IllegalArgumentException e) { list.setSelectedValue(t, true); throw e; } }
            TargetStore.getInstance().replace(targets); selectedId = editing == null ? null : editing.id; super.doOKAction();
        } catch (Exception e) { setErrorText(e.getMessage()); }
    }
    String selectedId() { return selectedId; }
    @Override protected void dispose() { disposed = true; super.dispose(); }
}
