# jitterentropy 3.7.0

Source: https://github.com/smuellerDD/jitterentropy-library

Release: v3.7.0, commit `e783cf1c450bce4d72f95c9f9c84546a6094976a`.
The public header and `src/` files are copied from that commit, with one
conditional change in `src/jitterentropy-base.c`: the DolphinDotNet port
disables automatic collector startup recovery and fails on the first source
health failure. This preserves the fixed memory/time/oversampling policy.
`jitterentropy-base-user.h` is replaced by the DolphinDotNet bare-metal port
in `../dnd_jitterentropy_port.h`; it removes operating-system discovery and
routes timing and allocations through the bounded native provider.

The dedicated build helper enforces `-O0 -fno-lto -fwrapv` on every upstream
translation unit. Do not compile these sources with the application's normal
optimization/LTO flags or disable the upstream optimization guard.

The BSD-style option in LICENSE is used. Redistributed SDK/runtime binaries
must retain this license in their accompanying materials.

The port includes upstream startup and continuous health checks. These checks
do not establish the entropy rate of GameCube hardware. Physical GameCube
raw-source and restart qualification remains outstanding; Dolphin and host
results are execution/failure tests, not physical entropy validation.
