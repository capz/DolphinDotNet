# Kart — Harbor Loop

A small, original 3D kart racer inspired by the approachable driving, drift
boosts and gliding jumps of handheld kart games. One island circuit, one kart,
three laps: start driving immediately. All meshes, colors, track markings and
bitmap lettering are generated in C#. There are no downloaded game assets.

## Drive

- Stick / D-pad: steer. **A** accelerates, **B** brakes.
- Hold **R** while turning at speed, charge the yellow meter, then release R
  for a mini turbo. Gold strips also boost; the far strip launches a jump.
- Drive through a cyan item box; press **L** to use the held item.
- Hold **X** in the air to glide and descend more slowly.
- **Y** restarts your run. **Start+B** rescues the kart to the road;
  driving too far off the island also triggers rescue.
- **Start+X** exits. **Z** toggles the existing statistics/network overlay.

No track, character or kart selection, no title menu, no asset files. Offline
play starts immediately, even without a BBA, DHCP server or another player.
The timer stops after three laps; Y begins another run. Ordered checkpoints
reject reverse start-line crossings. Grass slows the kart.

## Two-player peer-to-peer LAN play

Run Kart on two GameCubes, or two Dolphin instances with working BBA networking,
on the **same IPv4 broadcast LAN**. Each needs a distinct IP address. DHCP is
the default. Allow **UDP 28777** between devices and LAN broadcast discovery.
No server, account, Internet service or DNS lookup is used. A Dolphin BBA must
actually expose the other instance/device to the LAN; isolated emulated/NAT
networks do not necessarily forward broadcasts. Do not forward this port to
the public Internet.

The first compatible peer pairs automatically. After both acknowledge the
pair, each starts a fresh three-lap run with a three-second countdown. Your
kart is orange; the peer is cyan. The HUD shows peer status, peer lap, approximate position and a
mini-map. Starts are approximate LAN starts, not a synchronized competition
clock. Y restarts only your local run. This is a friendly two-player race/time
trial demonstration, without authoritative results, bots or kart-to-kart collisions.

The local kart remains responsive: fixed 60 Hz simulation, snapshots at 20 Hz,
interpolated peer rendering with at most 150 ms extrapolation. Remote karts
are non-colliding ghosts, avoiding contradictory collision outcomes. At half
a second without packets the HUD warns; after three seconds the remote kart
is removed and you keep driving offline. Pairing again begins a fresh race.
The game retries failed network setup/socket creation every five seconds,
without waiting in the driving loop. Native DHCP setup may take longer to
finish; it still runs in the background.

On LANs that suppress broadcast, set `KartNetwork.DirectPeer` to the other
machine's IPv4 address **on each device** and rebuild. `Port` defaults to 28777;
`KartPeer.Room` defaults to 701 and must match. Automatic discovery supports
one other player; additional copies should use another room/port. The 252-byte
versioned, big-endian packet validates bounds, session identity, sequence and
room before touching a remote visual. It is unauthenticated and intended for
a trusted LAN. A restarting peer can pair after the old three-second lease.

## Items

Six cyan boxes are spread around the circuit. Each kart has its own pickups,
so both players can collect from the same box. A collected box reappears after
six seconds; you carry one item at a time. Pickups cycle through three types:

- **Turbo:** immediately raises speed to at least 30 m/s and grants two seconds
  of boost. Grass still slows the kart.
- **Slick:** drops a pink road hazard behind the kart. It lasts 14 seconds.
- **Pulse:** fires a green projectile straight ahead at 32 m/s. It expires after
  four seconds or when it leaves the island; it does not home or bounce.

Slicks and pulses cause a short spin-out and quarter the victim's speed.
Brief invulnerability prevents stacked hits; gliding clears ground hazards.
Your own objects can hit you after a one-second grace period, so items remain
interactive offline. At most six objects per owner can be active; a full pool
keeps the held item until there is space. Items cannot fire during the countdown,
a spin-out or after finishing. Y/new paired races clear local objects/inventory.

Each peer publishes its active items repeatedly in its versioned UDP snapshot.
The victim checks collisions against that view, applies its own slowdown, and
repeats a per-slot acknowledgement until newer items replace it. The owner then
removes the consumed object. Unique item IDs survive local restarts, so delayed
acknowledgements cannot consume a reused slot or apply the same hit twice. New
peer identities clear old acknowledgement state. This tolerates ordinary packet
loss. Snapshots older than 250 ms cannot cause new hits, avoiding damage from
stale objects during a link outage. It does not implement lag compensation or an authoritative anti-cheat
service. Both devices must use this item-enabled protocol (KAR2); the earlier
48-byte demo will not pair with it.

## Build and validate

Open `Kart.sln` in Rider/Visual Studio, or run:

```sh
dotnet build samples/Kart/Kart.sln -c Release
dotnet run --project samples/Kart -c Release --no-build
```

The usual Dolphin Play/Debug launch profiles and Rider configurations are
included. See [IDE setup](../../docs/ide-workflow.md) for the toolchain.

`tests/KartRulesSmoke` verifies offline driving, braking, drift boost, gliding,
checkpoint order, rescue, malformed/reordered packets, pairing, timeout/rejoin
item pickups/use/hits and two real .NET UDP endpoints. `tests/KartNetworkSmoke` runs the production
network adapter through AOT/native POSIX sockets against
`tests/kart_network_peer.py`, including item snapshots/hit acknowledgements, loss and reconnection while driving.
`tests/KartInputSmoke` executes the real controller cache/router and driving code
through AOT after peer pairing, checking countdown, held acceleration, braking
and steering with forced GC. Numeric acceptance also covers floating-point fields
carried across branches, including PowerPC.
`tests/KartItemSmoke` also executes the item contracts through AOT with forced
GC/sanitizers and on PowerPC in Dolphin. Capture verifies the native kart, track
and cyan item-box rendering without networking.
Two physical BBA devices and cross-machine Dolphin LAN setup remain manual
acceptance; host loopback tests do not validate bridge/TAP configuration.

## API updates

Production pickups use a timestamp-seeded `Random`; explicit seeds reproduce
item choices in tests. Network retries and peer expiry use `Clock.Timestamp`,
independent of capped simulation steps. Race display and transmitted finish times
use a `Stopwatch`. Procedural engine audio follows speed/boost; pickups, item use
and hits have cues and rumble. Best race milliseconds are saved as a checksummed
`DKRT` record, merging the better time when a card is inserted in either slot.
No card or network is required for single-player play.
