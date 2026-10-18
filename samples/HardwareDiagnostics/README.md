# Live hardware inventory

Displays readable device names and presence, controller connection transitions,
and both memory-card ports. Card presence is polled every two seconds; controller
edges use the normal per-frame input snapshot. A repeats the extended scan.
Start+X reboots and Start+Y returns to the system menu; Z toggles statistics.
Cached labels avoid reformatting the inventory each frame. Unknown/unsupported
presence remains explicit instead of being presented as a confirmed device.

