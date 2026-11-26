# winget packaging

The `packaging/winget` tree holds the generated manifest set submitted to
[`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) for the
Windows CLI bundles.

Generate the manifests from a published GitHub release (the release assets must
already be uploaded so their SHA-256 digests are available):

```powershell
./scripts/update-winget-manifests.ps1 -Version <version>
```

This writes:

```
packaging/winget/manifests/p/PureLogicCode/CHDSharp/<version>/
  PureLogicCode.CHDSharp.yaml
  PureLogicCode.CHDSharp.installer.yaml
  PureLogicCode.CHDSharp.locale.en-US.yaml
```

Validate and install from the local manifest:

```powershell
winget validate --manifest .\packaging\winget\manifests\p\PureLogicCode\CHDSharp\<version>
winget install --manifest .\packaging\winget\manifests\p\PureLogicCode\CHDSharp\<version>
```

Submit the generated directory to the community repository with
[`wingetcreate`](https://github.com/microsoft/winget-create):

```powershell
wingetcreate submit --prtitle "New package: PureLogicCode.CHDSharp version <version>" --token $env:GH_TOKEN .\packaging\winget\manifests\p\PureLogicCode\CHDSharp\<version>
```

Older version directories are kept in place, mirroring `winget-pkgs`.
