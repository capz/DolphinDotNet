package com.dolphindotnet.rider;

import com.intellij.openapi.components.PathMacroManager;
import com.intellij.openapi.fileChooser.FileChooserDescriptorFactory;
import com.intellij.openapi.options.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.ui.*;
import com.intellij.ui.*;
import com.intellij.ui.components.*;
import com.intellij.util.ui.JBUI;
import javax.swing.*;
import javax.swing.table.DefaultTableModel;
import java.awt.*;
import java.nio.file.*;
import java.util.*;
import org.jetbrains.annotations.NotNull;

final class AttachEditor extends SettingsEditor<AttachConfiguration> {
    private final Project project;
    private final TextFieldWithBrowseButton folder = new TextFieldWithBrowseButton();
    private final ComboBox<TargetProfile> targets = new ComboBox<>();
    private final JBLabel elf = new JBLabel(), source = new JBLabel(), addresses = new JBLabel(), summary = new JBLabel();
    private final DefaultTableModel mappings = new DefaultTableModel(new String[]{"Build source prefix", "Local source prefix"}, 0);
    private final JTable table = new com.intellij.ui.table.JBTable(mappings);
    private String missingId;
    AttachEditor(Project project) { this.project = project; }
    @Override protected @NotNull JComponent createEditor() {
        var panel = Forms.form(); panel.setBorder(JBUI.Borders.empty(12));
        folder.addBrowseFolderListener(project, FileChooserDescriptorFactory.createSingleFolderDescriptor().withTitle("Debug build folder"));
        folder.getTextField().getDocument().addDocumentListener(new DocumentAdapter() { @Override protected void textChanged(@NotNull javax.swing.event.DocumentEvent e) { updateArtifacts(); } });
        Forms.row(panel, 0, "Debug build folder:", folder); Forms.row(panel, 1, "ELF:", elf);
        Forms.row(panel, 2, "Source map:", source); Forms.row(panel, 3, "Address map:", addresses);
        Forms.row(panel, 4, "", new TitledSeparator("Target connection"));
        var manage = new JButton("Manage targets…");
        manage.addActionListener(e -> {
            var selected = (TargetProfile)targets.getSelectedItem();
            var dialog = new TargetDialog(project, selected == null ? missingId : selected.id);
            if (dialog.showAndGet()) reloadTargets(dialog.selectedId());
        });
        targets.addActionListener(e -> updateSummary());
        Forms.row(panel, 5, "Target:", Forms.beside(targets, manage)); Forms.row(panel, 6, "", summary);
        var mappingPanel = new JPanel(new BorderLayout());
        var mappingContent = new JPanel(new BorderLayout()); mappingContent.add(new JBScrollPane(table));
        var add = new JButton("+"); var remove = new JButton("−");
        add.addActionListener(e -> mappings.addRow(new Object[]{"", ""}));
        remove.addActionListener(e -> { if (table.getSelectedRow() >= 0) mappings.removeRow(table.getSelectedRow()); });
        var toolbar = new JPanel(new FlowLayout(FlowLayout.LEFT)); toolbar.add(add); toolbar.add(remove); mappingContent.add(toolbar, BorderLayout.SOUTH);
        mappingContent.setPreferredSize(JBUI.size(500, 150));
        var hideable = new HideableDecorator(mappingPanel, "Source path mappings", false); hideable.setContentComponent(mappingContent);
        Forms.row(panel, 7, "", mappingPanel);
        Forms.row(panel, 8, "", new JBLabel("The target must already be running its debug build."));
        var root = new JPanel(new BorderLayout()); root.add(panel, BorderLayout.NORTH); return root;
    }
    @Override protected void resetEditorFrom(@NotNull AttachConfiguration config) {
        folder.setText(config.buildFolder); reloadTargets(config.targetId); mappings.setRowCount(0);
        config.sourceFileMap.forEach((build, local) -> mappings.addRow(new Object[]{build, local})); updateArtifacts();
    }
    @Override protected void applyEditorTo(@NotNull AttachConfiguration config) throws ConfigurationException {
        if (table.isEditing() && !table.getCellEditor().stopCellEditing()) throw new ConfigurationException("Finish editing source mappings.");
        var paths = new LinkedHashMap<String, String>();
        for (int i = 0; i < mappings.getRowCount(); i++) {
            var build = Objects.toString(mappings.getValueAt(i, 0), "").trim(); var local = Objects.toString(mappings.getValueAt(i, 1), "").trim();
            if (build.isEmpty() || local.isEmpty() || paths.put(build, local) != null) throw new ConfigurationException("Source mappings need unique build prefixes and nonempty local paths.");
        }
        config.buildFolder = folder.getText().trim();
        var target = (TargetProfile)targets.getSelectedItem(); config.targetId = target == null ? Objects.toString(missingId, "") : target.id;
        config.sourceFileMap.clear(); config.sourceFileMap.putAll(paths);
    }
    private void reloadTargets(String id) {
        missingId = id; targets.removeAllItems(); TargetProfile selected = null;
        for (var t : TargetStore.getInstance().targets()) { targets.addItem(t); if (Objects.equals(t.id, id)) selected = t; }
        targets.setSelectedItem(selected); updateSummary();
    }
    private void updateSummary() {
        var t = (TargetProfile)targets.getSelectedItem();
        summary.setText(t == null ? "Select a target or define one using Manage targets…" : t.summary());
    }
    private void updateArtifacts() {
        try {
            var found = BuildArtifacts.discover(Path.of(PathMacroManager.getInstance(project).expandPath(folder.getText())));
            elf.setText(found.elf().getFileName().toString()); source.setText(found.sourceMap().getFileName().toString()); addresses.setText(found.addressMap().getFileName().toString());
        } catch (Exception e) { elf.setText("—"); source.setText("—"); addresses.setText("—"); elf.setToolTipText(e.getMessage()); }
    }
}
