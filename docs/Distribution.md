# Windows distribution

SensorAnalysis, SensorAnalysis Space, and SensorAnalysis Sphere are published as separate Windows x64 applications from this repository. GitHub Releases hosts the files. [Velopack](https://docs.velopack.io/) builds the installer and the update packages. Each application ships with its own .NET runtime, so the user does not install .NET first.

The repository's default branch is `master`. The release workflow also accepts `main` if the default branch is renamed.

Authenticode signing is not part of this setup. No signing certificate and no extra secret are required.

## Products

`distribution/products.json` is the list of products. The id, Velopack pack id, and Velopack channel are the same string.

| Product id | Application | Tag |
| --- | --- | --- |
| `sensoranalysis` | `src/SensorAnalysis.App` | `sensoranalysis-vX.Y.Z` |
| `sensoranalysis.space` | `src/SensorAnalysis.Space.App` | `sensoranalysis.space-vX.Y.Z` |
| `sensoranalysis.sphere` | `src/SensorAnalysis.Sphere.App` | `sensoranalysis.sphere-vX.Y.Z` |

The CLI, hallway library, and viewport prototype are not packaged.

GitHub's `/releases/latest` is the newest release in the whole repository. It is the wrong way to find one of these products. A client lists releases and keeps tags that match `^(?<id>[a-z0-9]+(?:\.[a-z0-9]+)*)-v(?<version>\d+\.\d+\.\d+)$`. The highest stable version for each id is that product's latest release. Drafts and prereleases are ignored.

The manifest on `master` names the products and the version currently in source:

`https://raw.githubusercontent.com/marksn-ms/SensorAnalysis.NET/master/distribution/products.json`

Published files live on the GitHub Release for that tag. The manifest version can be ahead of the published release while a workflow is still running.

## Build a package locally

Install the .NET 8 SDK. No secrets are required.

```powershell
./distribution/pack.ps1 -ProductId sensoranalysis
./distribution/pack.ps1 -ProductId sensoranalysis.space
./distribution/pack.ps1 -ProductId sensoranalysis.sphere
```

The script publishes `win-x64` self-contained, then runs the pinned `vpk` tool from `dotnet-tools.json`. Velopack 1.2.161 is also the `Velopack` package version. Those two versions stay together.

Results:

- `artifacts/dist/<id>/` is the Velopack output. It can also contain an older full package when a delta was built.
- `artifacts/upload/<id>/` is the set that GitHub Releases receives, including `SHA256SUMS.txt`.

To try a delta locally, pack once, copy the `*-full.nupkg` aside, then pack again with a higher `-Version` and `-BasePackages` pointing at that copy. `-Version` is only for local experiments. GitHub Actions uses the manifest version.

`x64` is the only runtime. The applications are WPF, so they are Windows programs. arm64 is not built.

## Change a version and publish

1. Set the same `MAJOR.MINOR.PATCH` version in `distribution/products.json` and in that application's `.csproj` `<Version>`.
2. Open a pull request. CI builds and tests. It does not publish.
3. Merge to `master`.

The release workflow runs only when `distribution/products.json` changes on `master`. It compares that file with the previous commit:

- The commit that adds the manifest publishes nothing.
- An unchanged version publishes nothing.
- A higher version publishes that product only.
- A product added to an existing manifest is published at the version in that commit.
- A lower version fails the workflow and publishes nothing.
- A tag that already exists is left alone. The workflow fails rather than replacing it.

`distribution/ReleasePlan` is the program that makes this decision. `dotnet run --project distribution/ReleasePlan -- --help` explains it. It needs no secrets.

Failed tests, a failed package build, or a failed provenance step on a public repository stop the job before `gh release create`.

## Files on a release

Velopack puts the channel in the file name when the channel is not the default `win` channel. Each product has its own channel, so the names look like this:

| File | Purpose |
| --- | --- |
| `<id>-<id>-Setup.exe` | Per-user installer. It does not need administrator rights. |
| `<id>-<id>-Portable.zip` | Unzip and run. Useful when an installer is inconvenient. Velopack can still update this copy. |
| `<id>-<version>-<id>-full.nupkg` | Full update package. |
| `<id>-<version>-<id>-delta.nupkg` | Smaller update, present when the previous full package of the same product was available. |
| `releases.<id>.json` | Velopack feed for this product's channel. |
| `SHA256SUMS.txt` | SHA-256 of the other files. The checksum file is not listed inside itself. |

Each product installs under its own folder in `%LocalAppData%\<id>` and has its own Start menu shortcut. Installing one does not replace the others.

## Checksums

`SHA256SUMS.txt` uses lowercase hashes and two spaces before the file name.

```powershell
Get-Content SHA256SUMS.txt | ForEach-Object {
    if ($_ -match '^([0-9a-f]{64})  (.+)$') {
        $actual = (Get-FileHash $Matches[2] -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $Matches[1]) { throw "$($Matches[2]) does not match" }
    }
}
```

Run that command in the folder that contains the downloaded files.

## Provenance

On a public repository, the release job records a build provenance attestation with `actions/attest` before it creates the GitHub Release. Verify a downloaded file with:

```powershell
gh attestation verify .\sensoranalysis-Setup.exe --repo marksn-ms/SensorAnalysis.NET
```

This repository is private. GitHub Artifact Attestations for private repositories require GitHub Enterprise Cloud, so the workflow skips that step until the repository is public. The release notes say whether provenance was recorded.

## Updates

Each application calls `VelopackApp.Build().Run()` before WPF starts. After install, startup checks the product's own stable releases. Help → Check for updates does the same check and reports the result. When a newer version exists, the user can download it and restart. The application does not install an update without that confirmation.

The update source reads GitHub Releases whose tags belong to that product id, then reads `releases.<id>.json` from those releases. It does not use `/releases/latest`. A `dotnet run` build is not an installed copy, so the startup check stays quiet and the Help command explains that.

`SENSORANALYSIS_UPDATE_PATH` can point at a local Velopack output directory for a side-by-side experiment. Unset it for normal use.

SensorAnalysis skips the startup prompt when it is launched with recipe or capture arguments, so an unattended recording is not blocked by a dialog. The Help command still works.

## Troubleshooting

**SmartScreen or an unknown-publisher warning.** The installer is not signed. Windows may ask for confirmation. Signing can be added later with `vpk pack --signParams` or `--azureTrustedSignFile`. Do not commit certificates or passwords.

**The download or update returns 404.** Release assets on a private repository are not public. Make the repository public before asking other people to install or update.

**Check for updates says the application is not installed.** The running copy was built with `dotnet run` or copied out of `artifacts/publish`. Install `*-Setup.exe`, or run the portable package, then check again.

**One product offers the other product's version.** The installed channel and the tag prefix disagree. Package with `distribution/pack.ps1` so the pack id, channel, and tag prefix stay equal to the product id.

**The GitHub Release was not created.** The workflow publishes only after tests and packaging succeed, and only when the manifest version increased. A tag that already exists is not replaced. Bump the version again after deleting a bad release by hand, if that release should not stay up.

**An update downloads slowly or the window waits.** The full package contains the self-contained runtime. A delta is smaller, and it appears once a previous full package of the same product is on GitHub.

**Too many releases.** The update check walks up to 1,000 GitHub releases and keeps only this product's stable tags. `vpk download github` is not used, because that command follows the repository's latest release rather than one product.

## Add another application

1. Add an object to `distribution/products.json`. Use a new lowercase id. Set `packId` and `channel` to that same id, and set `version` to the first version you want published.
2. Set the same `<Version>` in the application project. Reference `Velopack` at the manifest's `velopackVersion`.
3. Give the WPF `App.xaml` a `Page` build action, set `StartupObject`, and call `VelopackApp.Build().Run()` before `InitializeComponent`.
4. Add Help → Check for updates, and call `ProductUpdates.OfferAsync` with the product id.
5. Build once with `distribution/pack.ps1` and confirm the Setup name, feed file, and install folder are distinct.
6. Merge the manifest change. The product is published because it is new. Later releases are version increases only.

Keep the Velopack package version and `dotnet-tools.json` on the same `vpk` version.
