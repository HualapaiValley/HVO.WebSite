# Policy snapshot maintenance

Start with [common policy](common.md) and [WebSite adoption](website.md).
The [manifest](manifest.json) identifies the candidate version and source. From
the repository root, verify the pinned portable files with:

```sh
shasum -a 256 -c docs/development/policy/snapshot.sha256
```

The manifest version identifies the snapshot revision; `policyVersion` identifies
the unchanged common-policy version. On an intentional policy change, bump the snapshot version and regenerate the checksum
file for exactly the manifest's file list; review both content and digest changes.
On distribution, copy that complete snapshot and compare version/digests against
the reviewed canonical commit. Do not regenerate hashes to hide an unexplained
mismatch. This local consistency check is not independent review, protected-branch
enforcement or a live adoption test. Existing repository skills and requirements
are pinned by the repository revision and are not portable common-policy files.
