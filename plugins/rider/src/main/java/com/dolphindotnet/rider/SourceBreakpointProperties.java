package com.dolphindotnet.rider;

import com.intellij.xdebugger.breakpoints.XBreakpointProperties;
import org.jetbrains.annotations.NotNull;

public final class SourceBreakpointProperties extends XBreakpointProperties<SourceBreakpointProperties> {
    @Override public SourceBreakpointProperties getState() { return this; }
    @Override public void loadState(@NotNull SourceBreakpointProperties state) { }
}
