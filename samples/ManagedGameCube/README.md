# Rotating managed GameCube scene

Rotation advances at 60 degrees per second using `FrameClock`, with frame delta
capped at 100 ms to avoid huge jumps after a stall. Rendering speed no longer
sets the intended rotation rate. Z toggles the shared statistics overlay.

