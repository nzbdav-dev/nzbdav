# Changelog

## [0.6.2](https://github.com/nzbdav-dev/nzbdav/compare/v0.6.1...v0.6.2) (2026-03-14)


### Bug Fixes

* compatability issues with NZBDonkey ([#316](https://github.com/nzbdav-dev/nzbdav/issues/316)) ([b2d0f2a](https://github.com/nzbdav-dev/nzbdav/commit/b2d0f2a4c6b48cca688bdffb91ba1b71a3fb1b84))
* downgrade unreachable Arr instance log level from Error to Debug ([#352](https://github.com/nzbdav-dev/nzbdav/issues/352)) ([90a03bf](https://github.com/nzbdav-dev/nzbdav/commit/90a03bf3e63a871b75d25ab109a6fcdd4689ffae))
* ensure `audio/flac` content-type mapping for flac files. ([5253fe3](https://github.com/nzbdav-dev/nzbdav/commit/5253fe3f03cbc2889928c338b2096acc7b863a52))
* fail queue items with missing nzb blobs instead of blocking queue ([#351](https://github.com/nzbdav-dev/nzbdav/issues/351)) ([a146d07](https://github.com/nzbdav-dev/nzbdav/commit/a146d07d8c62891993796b28ad358e41385dd02d))
* improve error message for malformed nzbs. ([325252e](https://github.com/nzbdav-dev/nzbdav/commit/325252e65f910f36d0e52810ccb2fba0d1a50019))
* typo when disposing queue nzb stream. ([3e44aae](https://github.com/nzbdav-dev/nzbdav/commit/3e44aaebd635f6dcd9949f1d6dcd80d61985cbb0))
* update changelog link on ui leftnav-menu. ([14cd09d](https://github.com/nzbdav-dev/nzbdav/commit/14cd09d2a5f88438b79b46cc6b9c1200fedf0c16))

## [0.6.1](https://github.com/nzbdav-dev/nzbdav/compare/v0.6.0...v0.6.1) (2026-03-11)


### Bug Fixes

* **deps:** bump @tailwindcss/vite from 4.1.11 to 4.2.1 in /frontend ([#330](https://github.com/nzbdav-dev/nzbdav/issues/330)) ([3389627](https://github.com/nzbdav-dev/nzbdav/commit/3389627c98a50370d580d614ebb0f0874d507219))
* **deps:** bump @types/express-serve-static-core ([#347](https://github.com/nzbdav-dev/nzbdav/issues/347)) ([95f8953](https://github.com/nzbdav-dev/nzbdav/commit/95f89533f1ed3f16a4c862f3e67f83d6b6ddf401))
* **deps:** bump @types/node from 20.19.10 to 25.4.0 in /frontend ([#328](https://github.com/nzbdav-dev/nzbdav/issues/328)) ([7239021](https://github.com/nzbdav-dev/nzbdav/commit/72390216d65380230fff1b0c091ec677e892a223))
* **deps:** bump bootstrap from 5.3.7 to 5.3.8 in /frontend ([#329](https://github.com/nzbdav-dev/nzbdav/issues/329)) ([1790518](https://github.com/nzbdav-dev/nzbdav/commit/17905189d379ae0d8ed0e2934d3acde7e3009785))
* **deps:** bump cross-env from 7.0.3 to 10.1.0 in /frontend ([#336](https://github.com/nzbdav-dev/nzbdav/issues/336)) ([b8d6693](https://github.com/nzbdav-dev/nzbdav/commit/b8d6693225e819127bb40063f335c8ab7a4f5ca0))
* **deps:** bump express and @types/express in /frontend ([#324](https://github.com/nzbdav-dev/nzbdav/issues/324)) ([1539ce5](https://github.com/nzbdav-dev/nzbdav/commit/1539ce5d50ac53f1ca39a65166d17ed80fb295e1))
* **deps:** bump isbot from 5.1.29 to 5.1.35 in /frontend ([#322](https://github.com/nzbdav-dev/nzbdav/issues/322)) ([2d0d069](https://github.com/nzbdav-dev/nzbdav/commit/2d0d0694ecc060134810e7c2d4bbb07aaa94a74f))
* **deps:** bump isbot from 5.1.35 to 5.1.36 in /frontend ([#349](https://github.com/nzbdav-dev/nzbdav/issues/349)) ([0619772](https://github.com/nzbdav-dev/nzbdav/commit/06197726fd2be0695027e5a7ca1ecf8c55d21586))
* **deps:** Bump Microsoft.AspNetCore.OpenApi from 10.0.1 to 10.0.4 ([#332](https://github.com/nzbdav-dev/nzbdav/issues/332)) ([7e0cfd6](https://github.com/nzbdav-dev/nzbdav/commit/7e0cfd6acada37b2b2de8961eae9d095a97f8417))
* **deps:** Bump Microsoft.EntityFrameworkCore.Design from 10.0.1 to 10.0.4 ([#334](https://github.com/nzbdav-dev/nzbdav/issues/334)) ([88fa597](https://github.com/nzbdav-dev/nzbdav/commit/88fa5976bda674e98d2bf57802fbddeb721abaaa))
* **deps:** Bump Microsoft.EntityFrameworkCore.Sqlite from 10.0.1 to 10.0.4 ([#338](https://github.com/nzbdav-dev/nzbdav/issues/338)) ([e19d72c](https://github.com/nzbdav-dev/nzbdav/commit/e19d72cd42b9ea302fc6e5dae32ea0e2652f1094))
* **deps:** bump mime-types from 3.0.1 to 3.0.2 in /frontend ([#323](https://github.com/nzbdav-dev/nzbdav/issues/323)) ([8866951](https://github.com/nzbdav-dev/nzbdav/commit/88669514ff6ff279647cd8f92f23ae9f3aa908a4))
* **deps:** bump react-dropzone from 14.3.8 to 15.0.0 in /frontend ([#348](https://github.com/nzbdav-dev/nzbdav/issues/348)) ([ab24e15](https://github.com/nzbdav-dev/nzbdav/commit/ab24e15c3b8ec3cda5c07c2943adbf1fadd1c52c))
* **deps:** bump tailwindcss from 4.1.11 to 4.2.1 in /frontend ([#335](https://github.com/nzbdav-dev/nzbdav/issues/335)) ([2a62a41](https://github.com/nzbdav-dev/nzbdav/commit/2a62a41e8b3b094f69bbb687bec775776530435b))
* **deps:** bump the react group in /frontend with 4 updates ([#346](https://github.com/nzbdav-dev/nzbdav/issues/346)) ([46a8a7b](https://github.com/nzbdav-dev/nzbdav/commit/46a8a7bc605033c8bf64bc159f9337425044b292))
* **deps:** bump the react-router group in /frontend with 5 updates ([#345](https://github.com/nzbdav-dev/nzbdav/issues/345)) ([83833f4](https://github.com/nzbdav-dev/nzbdav/commit/83833f4e35cacc7010368a9b0935d1ed6945b58f))
* **deps:** bump tsx from 4.20.3 to 4.21.0 in /frontend ([#326](https://github.com/nzbdav-dev/nzbdav/issues/326)) ([71974ec](https://github.com/nzbdav-dev/nzbdav/commit/71974eca1762fb72f5f9ecad181b33a8dacb413f))
* **deps:** bump typescript from 5.9.2 to 5.9.3 in /frontend ([#325](https://github.com/nzbdav-dev/nzbdav/issues/325)) ([1c692a6](https://github.com/nzbdav-dev/nzbdav/commit/1c692a66364cce5112f2c66bff55ec9ce400ba13))
* **deps:** bump vite from 6.3.5 to 7.3.1 in /frontend ([#337](https://github.com/nzbdav-dev/nzbdav/issues/337)) ([0f8eea6](https://github.com/nzbdav-dev/nzbdav/commit/0f8eea6db59d16a3aeaf4b611e8c6b8d94b77e00))
* **deps:** bump vite-tsconfig-paths from 5.1.4 to 6.1.1 in /frontend ([#341](https://github.com/nzbdav-dev/nzbdav/issues/341)) ([c396ad3](https://github.com/nzbdav-dev/nzbdav/commit/c396ad34a826ea1cc37cf2d29e30466031eb79be))
* **deps:** bump ws from 8.18.3 to 8.19.0 in /frontend ([#342](https://github.com/nzbdav-dev/nzbdav/issues/342)) ([f2fa35d](https://github.com/nzbdav-dev/nzbdav/commit/f2fa35d86ad03c73ba5584ba2ccb3c28f25ef34d))
