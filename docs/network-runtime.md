# Networking runtime

IPv4 UDP/TCP client and server sockets, DNS, bounded HTTP/1.1 and a reflection-free JSON reader now have managed implementations and AOT bindings. This is an explicitly selected API subset, not desktop .NET networking conformance.

## Startup and pumping

Start through `Dolphin.Network.NetworkRuntime.StartAsync(NetworkOptions, CancellationToken)`. Defaults use DHCP; static IPv4 configuration is also available. Configure DNS server addresses explicitly: this backend does not assume that the gateway provides DNS or that DHCP exposes its resolver addresses. Initialization runs on a native LWP worker on GameCube. Its records and stack contain no managed pointers.

An application must call `NetworkRuntime.Pump()` and `Dolphin.Threading.Tasks.Scheduler.Pump(16)` each frame. Pump services at most 32 registered operation steps and has a 1 ms soft elapsed-time target by default. IO steps transfer at most 4 KiB, except atomic UDP datagrams. Game updates, input and graphics remain owned by the application. Headless servers can run the same services without rendering.

Await operations from ordinary C# methods; do not wait synchronously for pending networking tasks on the application thread. Existing `Task.Wait` rejects a task that requires external service rather than silently blocking. `Task.Run` is cooperative and is not a worker pool.

Example configuration:

```csharp
var startup = NetworkRuntime.StartAsync(new NetworkOptions {
    DnsServers = new[] { Dolphin.Network.IPAddress.Parse("192.168.1.1") }
}, default);
// Continue pumping networking and the scheduler while startup is pending.
```

`IsReady` indicates a configured interface, not a successful Internet connection. Stop closes application-owned sockets and cancels outstanding operations. A running native configuration call cannot be killed: Stop rejects during configuration. The current libogc2 backend does not promise interface reset after configuration failure; restart the application to retry a failed native initialization.

## Compiled standard API subset

The AOT compiler maps the following `System.*` types onto Core implementations:

| API | Implemented subset |
| --- | --- |
| `IPAddress`, `IPEndPoint`, `EndPoint` | IPv4 dotted-decimal parsing, byte-array construction/access, endpoint construction, Any/Loopback/Broadcast. `Dolphin.Network.IPAddress.Format()` also provides formatting without Object.ToString lowering. |
| `Dns`, `IPHostEntry` | `GetHostAddressesAsync(string)`, `GetHostEntryAsync(string)`; A records, CNAME chains, TTL cache, bounded UDP retries and DNS-over-TCP fallback |
| `UdpClient`, `UdpReceiveResult` | Local-port/endpoint constructors, endpoint send, default-endpoint send, receive with sender endpoint, close/dispose |
| `TcpClient` | Default constructor, `ConnectAsync(IPAddress,int)`, `ConnectAsync(string,int)`, `GetStream`, selected socket options, close/dispose |
| `TcpListener` | Address/endpoint constructors, Start/backlog, local endpoint, `AcceptTcpClientAsync()`, Stop |
| `Socket` | IPv4 TCP/UDP constructor, Bind, Listen, ConnectAsync/AcceptAsync, byte-array send/receive, endpoints, NoDelay/Broadcast setters, close/dispose |
| `NetworkStream` | Async reads, partial-write-aware full async writes, flush/dispose; synchronous reads/writes and seeking intentionally unsupported |
| `SocketException` | Typed catches and captured native ErrorCode. Normalized lifecycle/limit/truncation failures have code zero. |
| `HttpClient` | GET, SendAsync, GetStringAsync, GetByteArrayAsync, GetStreamAsync, ResponseHeadersRead and bounded response buffering |
| HTTP messages/content | Request/response messages, GET/HEAD/POST/PUT/DELETE methods, headers, status, EnsureSuccessStatusCode, byte/string request content and streamed response content |

Only the listed overloads are established. In particular, newer `Memory<T>`/`ValueTask` overloads, socket event arguments, general Socket receive-from result overloads, URI object APIs and arbitrary handler/options APIs are not implemented. Dolphin APIs expose explicit cancellation variants in addition to the standard Task-returning baseline. Use the exact signature tests as the compatibility contract.

The cooperative `Stream.CopyToAsync` implementation now chains actual asynchronous reads and writes, allowing network/HTTP streams to copy into memory or supported storage streams.

## Transport and resources

Application-native handles are generation-tagged tokens, never raw descriptors. Accepted sockets are explicitly nonblocking. TCP EOF, empty UDP packets, WouldBlock and UDP truncation are distinct. IPv4 wire values are converted explicitly between portable integer representation and native byte order.

Limits: 12 application sockets, 32 pending native operations, 2 active requests per HTTP client, 1 pending read/write/connect/accept per relevant socket, 16 DNS cache entries. The libogc2 stack may impose lower effective limits; native failure remains visible. UDP receive defaults to 1,200 bytes and can be configured up to 65,507. Oversized datagrams are consumed and rejected rather than silently truncated. The native UDP staging buffer is approximately 64 KiB outside the managed heap.

Operations use a monotonic 10-second deadline by default, configurable through NetworkOptions. DNS uses bounded one-second retransmission intervals and alternates configured resolvers. Address fallback attempts currently use individual connect deadlines; there is no independently configurable connect/DNS timeout API yet.

Use `Socket.TryReceiveFrom(byte[],offset,count,int[] endpoint)` for allocation-free UDP polling, and `TrySendTo` for immediate nonblocking send. A -1 result means WouldBlock; a zero receive count is a valid empty datagram. Async convenience APIs allocate tasks/results and are appropriate for control traffic; reusable buffers are preferred for per-frame gameplay.

TCP is a byte stream; raw Socket.SendAsync may accept fewer bytes than requested. NetworkStream.WriteAsync loops until all bytes are accepted. Applications must define message framing; client/server sockets do not add a gameplay protocol, authentication, reliability over UDP, matchmaking or NAT traversal. LAN hosting works through bound UDP sockets and TCP listeners; Internet hosting requires port reachability or a relay.

## DNS

The stub resolver requires ASCII/punycode hostnames and resolves IPv4 A records through configured recursive servers. It validates source address/port, query ID, response flags and the echoed question, follows bounded CNAME chains, limits name compression traversal, validates record boundaries and caches results by the minimum relevant TTL. Cache results are copied for callers. DNS replies that indicate truncation switch to a length-framed TCP query.

This is ordinary DNS, not DNSSEC or encrypted DNS. Query IDs combine a local counter with monotonic ticks; a cryptographic randomness provider is not yet integrated. Do not treat DNS as an authenticated identity source. Concurrent lookup sharing and negative caching are not implemented.

## HTTP and JSON

HTTP uses an incremental parser over TCP. It supports fixed-length, chunked and close-delimited responses, informational responses, HEAD/no-body statuses, trailers, selected automatic GET/HEAD redirects, response headers and body streaming. Credentials are removed when redirects change authority. Redirect count defaults to five. Request framing headers are generated by the implementation and cannot be overridden. Ambiguous/duplicate framing, truncation, header injection and unsupported transfer/content encodings fail explicitly.

Headers are capped at 8 KiB/64 fields; chunk metadata at 256 bytes per line; trailers at 8 KiB. Convenience response buffering defaults to 64 KiB and can be configured through the Dolphin HttpClient API. Streamed bodies are capped at 16 MiB. Use ResponseHeadersRead or GetStreamAsync for large data. GetStringAsync validates UTF-8. Connections use `Connection: close`; pooling, cookies, proxy support and decompression are deferred.

`https://` fails with PlatformNotSupportedException. Verified TLS requires a native provider, trust material, credible time and adequate entropy; these are not implemented by this change. There is no insecure certificate bypass. HTTP/2, HTTP/3/QUIC, HttpListener/ASP.NET, reflection serializers and legacy WebRequest/WebClient remain outside this subset.

`Dolphin.Serialization.Json.JsonReader` is a bounded pull tokenizer for downloaded text: objects, arrays, property names, strings/escapes, numbers, booleans and null. It enforces grammar, depth 32, string length 4,096 and input length 65,536. Number tokens retain their lexical value; GetInt32 performs checked integer conversion. Read one token at a time to budget parsing. It has no reflection dependency or whole-document object graph; application code interprets tokens or builds explicit typed readers.

## Validation

`tests/NetworkSmoke` compiles ordinary standard networking calls through AOT and exercises UDP client/server reply, TCP listen/connect/accept/read/write/EOF, DNS UDP/TCP, HTTP JSON, chunked/informational responses, ResponseHeadersRead/CopyToAsync, truncated and ambiguous responses, cancellation and oversized datagrams. Deterministic local fixtures run in the same process environment through `tests/network_fixtures.py EXECUTABLE [ARGS]`.

Native tests cover empty datagrams, truncation, invalid ranges, stale handles, descriptor reuse and capacity exhaustion. CI runs generated C under ASan/UBSan and GC stress on 64-bit hosts, GC stress on 32-bit hosts, and compiles/links the networking AOT program with the pinned GameCube toolchain. Actual Dolphin/BBA and GameCube execution remain separate hardware acceptance gates.
