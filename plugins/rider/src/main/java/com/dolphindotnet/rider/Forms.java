package com.dolphindotnet.rider;

import com.intellij.util.ui.JBUI;
import javax.swing.*;
import java.awt.*;

final class Forms {
    static JPanel form() { return new JPanel(new GridBagLayout()); }
    static void row(JPanel panel, int y, String label, JComponent field) {
        var c = new GridBagConstraints(); c.gridy = y; c.anchor = GridBagConstraints.NORTHWEST;
        c.insets = JBUI.insets(8, 0, 8, 16); c.gridx = 0;
        panel.add(new JLabel(label), c); c.gridx = 1; c.weightx = 1; c.fill = GridBagConstraints.HORIZONTAL;
        panel.add(field, c);
    }
    static JPanel beside(JComponent main, JComponent button) {
        var panel = new JPanel(new BorderLayout(JBUI.scale(8), 0));
        panel.add(main, BorderLayout.CENTER); panel.add(button, BorderLayout.EAST); return panel;
    }
}
