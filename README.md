# FinalScan

Quest 3 / 3S native realtime metric surfel scanner (Unity host + native C++/Vulkan core).

Authority: `FINALSCAN_V4_CONTRACT.md`. C00 inputs: `FINALSCAN_V3_REVIEW.md`, `VULKAN_DONOR_LEDGER.md`,
`FEATURE_PARITY.md`, `MIGRATION_LEDGER.md`, `RISK_REGISTER.md`, `SOTA_RESEARCH_LEDGER.md`.

Layout: `Runtime/` (C#, asmdef FinalScan.Runtime), `Editor/` (host setup + build), `Native~/` (C++ plugin
`libFinalScanNative.so`), `Shaders/`, `Tests/`, `Tools~/` (dev environment, host creation, build, deploy, probes).

Dev environment: `Tools~/dev_environment.sh` (Unity 6000.5.9f1 on /mnt/kingston-unity, host project
`FinalScanHost`, builds under `/mnt/kingston-unity/Builds/FinalScan`). Package repo stays small: no Library,
no APKs, no `.so` in git.
