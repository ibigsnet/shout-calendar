# Building and validating

Use a .NET 10 SDK and Dalamud API 15 references. `global.json` accepts the official builder's 10.0.101 SDK and newer serviced feature bands. Dependency lock files are tracked for the core, plugin and tests.

```sh
dotnet test src/ShoutCalendar.Tests/ShoutCalendar.Tests.csproj -c Release -p:RestoreLockedMode=true
dotnet build src/ShoutCalendar/ShoutCalendar.csproj -c Release -p:IsPlogonBuild=True -p:RestoreLockedMode=true -p:DalamudLibPath=/path/to/dalamud/
```

CI runs the tests with 10.0.101 and the latest serviced 10.0 SDK, then downloads official Dalamud references and builds with submission flags. It records the reference archive's SHA-256 in its logs. The archive tracks upstream Dalamud, so save the exact reference archive/hash when reproducing a release.

For release, validate the paired Shout Calendar Sync build from its standalone checkout. Sync vendors a reviewed commit of this core; refresh that manifest after committing shared-core changes. Both plugins need this update for the new framework-thread IPC endpoints, narrow status updates and canonical time-zone fields. Calendar still works independently when Sync is absent.

## Private public-relay configuration

Official Plogon builds use the encrypted manifest secret `public_relay_host`, exposed to MSBuild as `PLOGON_SECRET_public_relay_host`. It supplies the same setting as the private local configuration below; the explicit `SHOUT_PUBLIC_RELAY_HOST` setting takes precedence. Submit matching encrypted settings for both plugins so official builds have a working Public relay without a plaintext endpoint in the repository.

The operator endpoint is not tracked in source. For a packaged build, set `SHOUT_PUBLIC_RELAY_HOST` in the private build environment, or create a repository-root `PublicRelay.local.props` (ignored by Git) with:

```xml
<Project>
  <PropertyGroup>
    <SHOUT_PUBLIC_RELAY_HOST>relay.example</SHOUT_PUBLIC_RELAY_HOST>
  </PropertyGroup>
</Project>
```

Replace the example privately with the operator's DNS hostname (without a scheme, path or port). The public service uses HTTPS on 443. Build both plugins with the same setting. An environment value takes precedence over the local file. Unconfigured builds use the reserved `public-relay.invalid` placeholder and can still use Custom relays; ordinary CI builds do not need production infrastructure details. Configure private release jobs separately before distributing a package.

The value becomes assembly metadata in the generated binary. This keeps it out of tracked source and routine displays, **not secret from a client that connects to it**. Do not upload build logs, binlogs, `obj` output, private props, `.env` files, local configs or deployment backups without checking them. Keep real addresses, internal IPs and machine paths out of examples. Existing Git history is not sanitized by changing current files; review it before publication or a separately approved history rewrite.

Before public release, exercise the actual game host: accept/edit/hide while sync is fetching, reload both plugins, verify an accepted shared event's warning, compare a Tokyo/Eastern dated invite, recurring events across daylight saving time, overnight continuation chips, and unchecked chat channels. Automated tests cover the core cases; they cannot reproduce Dalamud's full lifecycle or rendering. Version, changelog, package hashes and release/submission manifests are finalized only after that check.
