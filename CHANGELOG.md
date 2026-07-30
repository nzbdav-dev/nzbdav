# Changelog

## [0.9.1](https://github.com/nzbdav/nzbdav/compare/v0.9.0...v0.9.1) (2026-07-28)


### Features

* **health:** add discard action and retained stream-trace UI status ([aeffee5](https://github.com/nzbdav/nzbdav/commit/aeffee59987109dd11d10f1bbd82a268da76d621)), closes [#685](https://github.com/nzbdav/nzbdav/issues/685)
* **health:** support packs report peak CPU during playback, not just an idle snapshot ([#689](https://github.com/nzbdav/nzbdav/issues/689)) ([6b312ab](https://github.com/nzbdav/nzbdav/commit/6b312ab83d8e3d6c7fd829d0083c5506bbbe7632)), closes [#679](https://github.com/nzbdav/nzbdav/issues/679)
* **health:** support packs show CPU, memory and per-stage playback stalls ([#678](https://github.com/nzbdav/nzbdav/issues/678)) ([0aae628](https://github.com/nzbdav/nzbdav/commit/0aae628ae365e3e3d31179225bce24caace82e7a))
* **ui:** capture stream traces from the UI without restarting the container ([#676](https://github.com/nzbdav/nzbdav/issues/676)) ([769b1cc](https://github.com/nzbdav/nzbdav/commit/769b1cc20604610f1e3c58cb36e158d859580e23))
* **ui:** confirm before turning off stream tracing so captured traces are not lost ([#688](https://github.com/nzbdav/nzbdav/issues/688)) ([b9fa452](https://github.com/nzbdav/nzbdav/commit/b9fa45231b44781b26621947d663d8581804b487))
* **usenet:** size queue connections as a share of the provider pool ([#698](https://github.com/nzbdav/nzbdav/issues/698)) ([b0eff0b](https://github.com/nzbdav/nzbdav/commit/b0eff0bc6ece4a261cfebabffc472b6d6c3c2dcb))


### Bug Fixes

* **api:** support packs keep streaming warnings instead of filling with Watchtower noise ([#675](https://github.com/nzbdav/nzbdav/issues/675)) ([f3f6347](https://github.com/nzbdav/nzbdav/commit/f3f63478070744cd1bb394da663f213283947fe0))
* **arr:** aggregate stuck-queue removals into one Warning per release ([3c9a59f](https://github.com/nzbdav/nzbdav/commit/3c9a59f0accb9c6cde79a37d71f8ce83711cebfe)), closes [#684](https://github.com/nzbdav/nzbdav/issues/684)
* **db:** backend no longer starts against a stale schema or hangs on a stale migration lock ([#701](https://github.com/nzbdav/nzbdav/issues/701)) ([6e15ddc](https://github.com/nzbdav/nzbdav/commit/6e15ddcc09c67ef92e2d5e4162debe4850450c8a))
* **deps:** Bump actions/setup-python in the github-actions group ([#694](https://github.com/nzbdav/nzbdav/issues/694)) ([dab9146](https://github.com/nzbdav/nzbdav/commit/dab91469441c37ba3da4459ce6a00cdb78200895))
* **deps:** Bump the npm-minor-and-patch group ([7121949](https://github.com/nzbdav/nzbdav/commit/71219491f051c3f9f2e3501326b240f8eff16d92))
* **deps:** Bump the npm-minor-and-patch group in /frontend with 3 updates ([563562c](https://github.com/nzbdav/nzbdav/commit/563562cbc68e91a98c0257df70706845bd1cff98))
* **health:** attribute range stalls to the range that started the fetch ([2fb1dfb](https://github.com/nzbdav/nzbdav/commit/2fb1dfb63c44fcd55833585fd878bcd825a7e653)), closes [#683](https://github.com/nzbdav/nzbdav/issues/683)
* **health:** keep diagnostics faithful under write storms, circuit misses, and scrubbing ([588c462](https://github.com/nzbdav/nzbdav/commit/588c4627a12cb1226d2f49346bdc2c948a0658c2))
* **health:** keep stream traces after recording stops for support packs ([155bde3](https://github.com/nzbdav/nzbdav/commit/155bde393a88d6d6ab6f899d2f776a4c16b4d7c8)), closes [#685](https://github.com/nzbdav/nzbdav/issues/685)
* **health:** keep the RangeEnd trace event when a read fails before the range opens ([a8b4f14](https://github.com/nzbdav/nzbdav/commit/a8b4f1466ce10fb2e2ba342c726d32aca4b71d95)), closes [#683](https://github.com/nzbdav/nzbdav/issues/683)
* **nntp:** stop a missing article from resetting an open provider circuit breaker ([c5f686a](https://github.com/nzbdav/nzbdav/commit/c5f686a79a6da67bda4a4c5adc899361c3935b8e)), closes [#682](https://github.com/nzbdav/nzbdav/issues/682)
* **test:** isolate Arr warning assertions from parallel Serilog noise ([954ce1f](https://github.com/nzbdav/nzbdav/commit/954ce1f637b1a3de6870855be2a36da1b21b5a02)), closes [#684](https://github.com/nzbdav/nzbdav/issues/684) [#685](https://github.com/nzbdav/nzbdav/issues/685)
* **ui:** anchor activity chart articles runs to leading and trailing zeros ([409297b](https://github.com/nzbdav/nzbdav/commit/409297b8d797ec011325863b366300a5140b7f40))
* **ui:** hide activity chart articles line when idle ([7d43ca4](https://github.com/nzbdav/nzbdav/commit/7d43ca4c696725d577cb2547cf02c5d2ed52d914))
* **ui:** left-align developer stream tracing controls on Support settings ([#708](https://github.com/nzbdav/nzbdav/issues/708)) ([5947656](https://github.com/nzbdav/nzbdav/commit/59476563b8080b0581ec055cbd2b0ec3d650fff1))
* **ui:** skip zero buckets on activity chart articles line ([1aa70ba](https://github.com/nzbdav/nzbdav/commit/1aa70ba4f44ab993dc1586a98c82d77909124a9e))
* **ui:** Test Conn shows failure reasons and works with saved Arr/rclone credentials ([0e3c649](https://github.com/nzbdav/nzbdav/commit/0e3c649d853ffac7f315469d5151f6c3b67f7ab0))
* **ui:** Test Conn works with saved Arr/rclone credentials and shows failure reasons ([e3751bb](https://github.com/nzbdav/nzbdav/commit/e3751bb8b8c0789be133f6bb562eb222992c932f))
* **usenet:** prefer the configured primary over larger idle backup pools ([#697](https://github.com/nzbdav/nzbdav/issues/697)) ([6fc0d7a](https://github.com/nzbdav/nzbdav/commit/6fc0d7a4d415fdfe129e99c38378f9fef64770f7))
* **webdav:** metadata-writing clients no longer flood logs against the read-only mount ([#687](https://github.com/nzbdav/nzbdav/issues/687)) ([af18a69](https://github.com/nzbdav/nzbdav/commit/af18a6918956b7b22b1f5de0441ff42e50786aa6))
* **webdav:** tell WebDAV clients why a deep directory listing was refused ([#702](https://github.com/nzbdav/nzbdav/issues/702)) ([49f6ddc](https://github.com/nzbdav/nzbdav/commit/49f6ddcfd0f58cad81c12433404a7afd6f25c6cc))
* **webdav:** throttle read-only write rejections per mount instead of per directory ([eaa9423](https://github.com/nzbdav/nzbdav/commit/eaa9423e1ea2b2ea2855749aa7ada52dcb370423)), closes [#680](https://github.com/nzbdav/nzbdav/issues/680)


### Performance Improvements

* **webdav:** lower memory use while streaming large files ([#690](https://github.com/nzbdav/nzbdav/issues/690)) ([5857d0b](https://github.com/nzbdav/nzbdav/commit/5857d0b6d22b8500a838063ee4dc9dbe3eb77ba2))

## [0.9.0](https://github.com/nzbdav/nzbdav/compare/v0.8.1...v0.9.0) (2026-07-26)


### ⚠ BREAKING CHANGES

* **usenet:** allow trusted providers and indexers with invalid TLS certificates ([#566](https://github.com/nzbdav/nzbdav/issues/566))

### Features

* **config:** configure all Settings via authoritative NZBDAV_CONFIG environment variables ([#590](https://github.com/nzbdav/nzbdav/issues/590)) ([f80d123](https://github.com/nzbdav/nzbdav/commit/f80d123c74fc262c1e1ee1abffe61dbbd7088186))
* **health:** download redacted support packs from Settings ([#610](https://github.com/nzbdav/nzbdav/issues/610)) ([ff6e143](https://github.com/nzbdav/nzbdav/commit/ff6e14360ee76732bd0ce6d3cde960ff5d71eaeb))
* **queue:** process multiple NZB downloads at once ([#591](https://github.com/nzbdav/nzbdav/issues/591)) ([fc4ae06](https://github.com/nzbdav/nzbdav/commit/fc4ae06b2104f7419af87b33063f3be8214dd709))
* **sab:** let Sonarr and Radarr pause, resume, and set a speed limit ([#648](https://github.com/nzbdav/nzbdav/issues/648)) ([d84e90c](https://github.com/nzbdav/nzbdav/commit/d84e90cb934b43639987d73558a322e8a77cf9d9))
* **ui:** group usenet providers by storage group with type-colored cards ([#652](https://github.com/nzbdav/nzbdav/issues/652)) ([18f99ea](https://github.com/nzbdav/nzbdav/commit/18f99eab1cf9af92d763dddcbab81ff9ac044f67))
* **ui:** identify the client on 4xx/5xx request-log lines ([#551](https://github.com/nzbdav/nzbdav/issues/551)) ([56cdf82](https://github.com/nzbdav/nzbdav/commit/56cdf82cc30845b670c5257b92e9c12b7475b93a))
* **ui:** improve usenet provider settings and storage group suggestions ([#655](https://github.com/nzbdav/nzbdav/issues/655)) ([63adeec](https://github.com/nzbdav/nzbdav/commit/63adeec5d5aa10b50b03fc61c2f2a31279f2d024))
* **usenet:** allow trusted providers and indexers with invalid TLS certificates ([#566](https://github.com/nzbdav/nzbdav/issues/566)) ([1f26282](https://github.com/nzbdav/nzbdav/commit/1f26282a90dbe0536f114b3f710332ebe72ad607))
* **usenet:** stop re-probing providers that already reported an article missing ([#649](https://github.com/nzbdav/nzbdav/issues/649)) ([7103abe](https://github.com/nzbdav/nzbdav/commit/7103abef31371310037ed529b9b6fa99df82473a))


### Bug Fixes

* **arr:** stop hung Radarr or Sonarr hosts from blocking shutdown and monitoring ([#619](https://github.com/nzbdav/nzbdav/issues/619)) ([2071127](https://github.com/nzbdav/nzbdav/commit/207112781000f3791c2e93f164d9161af74ae55b))
* **arr:** treat stale Arr cache 404s as misses during remove-and-search ([#624](https://github.com/nzbdav/nzbdav/issues/624)) ([d4909f2](https://github.com/nzbdav/nzbdav/commit/d4909f2d8583bf14c9c36b3bc7006f5c4d436775))
* **config:** reject unknown NZBDAV_CONFIG JSON properties ([#626](https://github.com/nzbdav/nzbdav/issues/626)) ([25a4f31](https://github.com/nzbdav/nzbdav/commit/25a4f313ee94ae6d0fb22e8eecbed3537eff9f13))
* **db:** deny unsafe SQLite operations during restore import ([#615](https://github.com/nzbdav/nzbdav/issues/615)) ([3495793](https://github.com/nzbdav/nzbdav/commit/34957937f90426fbe4c7cda06e74f2f7b065f2b4))
* **db:** prevent oversized backups from exhausting disk or memory during restore ([#616](https://github.com/nzbdav/nzbdav/issues/616)) ([fc7563c](https://github.com/nzbdav/nzbdav/commit/fc7563c4989041d1140c3cc657577678c77db598))
* **deps:** bump NzbDav.UsenetSharp to 3.3.0 ([#661](https://github.com/nzbdav/nzbdav/issues/661)) ([137764a](https://github.com/nzbdav/nzbdav/commit/137764aec2fb716530c81d934724bc046956e3d8))
* **deps:** bump react-router packages to 8.3.0 ([#632](https://github.com/nzbdav/nzbdav/issues/632)) ([e69a8f3](https://github.com/nzbdav/nzbdav/commit/e69a8f316026149c1d47a6f5861d55d05df05ca6))
* **deps:** bump the github-actions group across 1 directory with 3 updates ([#565](https://github.com/nzbdav/nzbdav/issues/565)) ([a6d87be](https://github.com/nzbdav/nzbdav/commit/a6d87bea666bd7c09cb63ada10fe3247b859983e))
* **deps:** Bump the github-actions group with 2 updates ([#631](https://github.com/nzbdav/nzbdav/issues/631)) ([4d176e2](https://github.com/nzbdav/nzbdav/commit/4d176e2d0dd6d305681368a8c6c5b1988f1a37a2))
* **deps:** Bump the npm-minor-and-patch group ([#630](https://github.com/nzbdav/nzbdav/issues/630)) ([bc74707](https://github.com/nzbdav/nzbdav/commit/bc74707fb34709f7996da27396095cca7cc8d0fa))
* **deps:** Bump the nuget-minor-and-patch group with 2 updates ([#564](https://github.com/nzbdav/nzbdav/issues/564)) ([116cfe4](https://github.com/nzbdav/nzbdav/commit/116cfe4169c25f797a3e20ad06a068fbf2067ae5))
* **deps:** bump ws in /frontend in the npm-minor-and-patch group ([#563](https://github.com/nzbdav/nzbdav/issues/563)) ([f493f53](https://github.com/nzbdav/nzbdav/commit/f493f538fb8d8b9087fec7b571a165858b11268b))
* **deps:** Bump zensical from 0.0.50 to 0.0.51 in the docs-python group ([#629](https://github.com/nzbdav/nzbdav/issues/629)) ([cf1a4bc](https://github.com/nzbdav/nzbdav/commit/cf1a4bc43365e2879eb57b70c728c36a298b0d1b))
* **deps:** npm audit fix ([b81f937](https://github.com/nzbdav/nzbdav/commit/b81f937f0316bf549fad80a7f9c86deb997a8e5a))
* **health:** allow urgent repairs for files still linked to SAB history ([#571](https://github.com/nzbdav/nzbdav/issues/571)) ([3ef8449](https://github.com/nzbdav/nzbdav/commit/3ef8449ca3d6b72de32f8e478dcf51cce8d48c5e)), closes [#568](https://github.com/nzbdav/nzbdav/issues/568)
* **health:** defer unexpected item failures to prevent queue starvation ([#606](https://github.com/nzbdav/nzbdav/issues/606)) ([4947b43](https://github.com/nzbdav/nzbdav/commit/4947b43959d4e18aa971305498360c3593faabd9))
* **health:** delay streaming repairs until failure threshold ([#621](https://github.com/nzbdav/nzbdav/issues/621)) ([a16b4fb](https://github.com/nzbdav/nzbdav/commit/a16b4fb7efc51af79c048a85e3356d503fc793eb))
* **nntp:** route pooled requests by free capacity ([#611](https://github.com/nzbdav/nzbdav/issues/611)) ([cb5d9cd](https://github.com/nzbdav/nzbdav/commit/cb5d9cd8340d69d49f2e28f236614ab801fd04e7))
* **nntp:** stop provider selection spending the half-open probe slot ([#549](https://github.com/nzbdav/nzbdav/issues/549)) ([69755e7](https://github.com/nzbdav/nzbdav/commit/69755e7f00adc125a2aab0d4f71f407d76a9a810))
* **queue:** consume the awaken signal outside an unreachable catch ([#625](https://github.com/nzbdav/nzbdav/issues/625)) ([85e6364](https://github.com/nzbdav/nzbdav/commit/85e636497047f85e306cbec3628ca32db2c92f34))
* **queue:** container no longer boot-loops on startup while restoring search play links ([#666](https://github.com/nzbdav/nzbdav/issues/666)) ([42e8891](https://github.com/nzbdav/nzbdav/commit/42e88911e107fab6cf9718fdfbc1580b7bd0fd34))
* **queue:** drop stranded TMP_LINKED_FILES_UNIQUE before rebuilding linked-id table ([#662](https://github.com/nzbdav/nzbdav/issues/662)) ([d9e4420](https://github.com/nzbdav/nzbdav/commit/d9e4420d4949a3617c77c3d4887c1d482f65af06))
* **queue:** make queue and WebDAV requests fail safely ([#608](https://github.com/nzbdav/nzbdav/issues/608)) ([b7fb6cb](https://github.com/nzbdav/nzbdav/commit/b7fb6cba232502b8b82d256ecff48148f807411a))
* **queue:** prevent concurrent worker starvation ([#607](https://github.com/nzbdav/nzbdav/issues/607)) ([9223dce](https://github.com/nzbdav/nzbdav/commit/9223dce8d672530d668be7859011831bbe682c84))
* **rclone:** log RC timeouts and connection failures without stack dumps ([#603](https://github.com/nzbdav/nzbdav/issues/603)) ([d16c2d1](https://github.com/nzbdav/nzbdav/commit/d16c2d1f0524060d39c4d12d5c35135dd249fc5b))
* restore preview and attachments on bug report logs field ([0e3a062](https://github.com/nzbdav/nzbdav/commit/0e3a062892f84d91a826505af2c2a1942370a3d3))
* **search:** bound NZB response and cache memory ([#614](https://github.com/nzbdav/nzbdav/issues/614)) ([4335d47](https://github.com/nzbdav/nzbdav/commit/4335d478f51f744db7f8485f81a06aa2cded9b30))
* **test:** remove native yEnc dependency from prefetch budget tests ([#609](https://github.com/nzbdav/nzbdav/issues/609)) ([452e845](https://github.com/nzbdav/nzbdav/commit/452e845a07ca8ac62921b6009d1b841335ea0533))
* **ui:** abort proxied transfers whose backend response ended incomplete ([#642](https://github.com/nzbdav/nzbdav/issues/642)) ([163f722](https://github.com/nzbdav/nzbdav/commit/163f72299df86601e3253d5b043e34b883ace44c))
* **ui:** color overview charts only when activity occurs ([#659](https://github.com/nzbdav/nzbdav/issues/659)) ([4124ac0](https://github.com/nzbdav/nzbdav/commit/4124ac08539053d4046bc4e743a1e48d86522fcf))
* **ui:** keep auto-tune confidence tooltip inside the provider modal ([#561](https://github.com/nzbdav/nzbdav/issues/561)) ([9b28633](https://github.com/nzbdav/nzbdav/commit/9b28633d7a97d1d4bd7415dd3727511d3ca281bc))
* **ui:** keep first-in-card settings tooltips from clipping ([#669](https://github.com/nzbdav/nzbdav/issues/669)) ([50020f3](https://github.com/nzbdav/nzbdav/commit/50020f3cb9319b3f20c4e461e9b0c0fb9d665537))
* **ui:** keep history failed status tooltips above table rows ([#644](https://github.com/nzbdav/nzbdav/issues/644)) ([4c3de8d](https://github.com/nzbdav/nzbdav/commit/4c3de8d36f75562f930d2530a77f5a45cc5134d0))
* **ui:** keep queue and history totals live over websocket ([#654](https://github.com/nzbdav/nzbdav/issues/654)) ([afbd5c8](https://github.com/nzbdav/nzbdav/commit/afbd5c8f8769d14dce94fe6e3e0e56dbd70ece37))
* **ui:** keep Test Connection available when editing Usenet providers ([#554](https://github.com/nzbdav/nzbdav/issues/554)) ([f939c97](https://github.com/nzbdav/nzbdav/commit/f939c97291f738cdb3f409a793e8931cd442c911)), closes [#553](https://github.com/nzbdav/nzbdav/issues/553)
* **ui:** prefill provider port and max connections defaults ([#643](https://github.com/nzbdav/nzbdav/issues/643)) ([6e52aee](https://github.com/nzbdav/nzbdav/commit/6e52aee54a7203d46ae40cd1adae57028bb9d765))
* **ui:** prevent queue category badges from wrapping ([#597](https://github.com/nzbdav/nzbdav/issues/597)) ([6a9002d](https://github.com/nzbdav/nzbdav/commit/6a9002d6d4d0210c1b15e241cd0c6721d579a23b))
* **ui:** recover failed uploads and preserve encoded Explore paths ([#605](https://github.com/nzbdav/nzbdav/issues/605)) ([f3af128](https://github.com/nzbdav/nzbdav/commit/f3af128e239f4d7a7fd1b1a56cc7e5fc45818b06))
* **ui:** restore frontend startup after compression-filter ESM import failure ([#600](https://github.com/nzbdav/nzbdav/issues/600)) ([eec0df9](https://github.com/nzbdav/nzbdav/commit/eec0df984410df25fae515dcf7fe0e5cde661f14))
* **ui:** show provider outages at their true time scale ([#657](https://github.com/nzbdav/nzbdav/issues/657)) ([af21c94](https://github.com/nzbdav/nzbdav/commit/af21c94e86722106016ecbb80dfd6c30c94abce0))
* **ui:** stop daisyUI navbar 50% split from clipping header controls ([#656](https://github.com/nzbdav/nzbdav/issues/656)) ([71229b0](https://github.com/nzbdav/nzbdav/commit/71229b001841e58f75a2f99a19799cff5d955651))
* **ui:** stop dumping BackendUnavailableError stacks when the API is unreachable ([#645](https://github.com/nzbdav/nzbdav/issues/645)) ([48a8ac1](https://github.com/nzbdav/nzbdav/commit/48a8ac138de392b2ea5db10e3de2fb4354311b97))
* **ui:** stop MaxListenersExceededWarning spam when refreshing the UI ([#598](https://github.com/nzbdav/nzbdav/issues/598)) ([25f7015](https://github.com/nzbdav/nzbdav/commit/25f7015487af3e0b794f9b8b597619f1ce7403ef))
* **usenet:** call the protected base overload when disposing streams ([#668](https://github.com/nzbdav/nzbdav/issues/668)) ([26c0ac7](https://github.com/nzbdav/nzbdav/commit/26c0ac74d23d929752118562b9b9862a51305574))
* **usenet:** prevent container OOM from unbounded WebDAV article buffers ([#651](https://github.com/nzbdav/nzbdav/issues/651)) ([754de39](https://github.com/nzbdav/nzbdav/commit/754de394a0f383d429f67afff7aa26a53d3438b6))
* **usenet:** show real Usenet failure reasons instead of opaque status 9 ([#646](https://github.com/nzbdav/nzbdav/issues/646)) ([68203d1](https://github.com/nzbdav/nzbdav/commit/68203d1228596f7b559fde717e3a283a5675eef1))
* **usenet:** stop cascade from pinning busy primaries and inflating article latency ([#650](https://github.com/nzbdav/nzbdav/issues/650)) ([3bd52a4](https://github.com/nzbdav/nzbdav/commit/3bd52a4582a807d77ede5fb7fc666622a0ebc4e8))
* **usenet:** stop playback corrupting halfway through a file when a segment fails ([#640](https://github.com/nzbdav/nzbdav/issues/640)) ([d7c467c](https://github.com/nzbdav/nzbdav/commit/d7c467c0a6537f9573e32a5f34fc8840cfb69594))
* **usenet:** streaming no longer wedges until restart after a corrupt article ([#663](https://github.com/nzbdav/nzbdav/issues/663)) ([3c9ecf9](https://github.com/nzbdav/nzbdav/commit/3c9ecf9b76edb465e81f7ad54c164e0e5d971503))
* **utils:** make Linux library scanning argv-safe and record-safe ([#612](https://github.com/nzbdav/nzbdav/issues/612)) ([bba1643](https://github.com/nzbdav/nzbdav/commit/bba164325bc1d1b2558346269e5cf7e3f47e163d))
* **warden:** cap decompressed source bytes and record length ([#617](https://github.com/nzbdav/nzbdav/issues/617)) ([dc6dab4](https://github.com/nzbdav/nzbdav/commit/dc6dab48243276b22a239935273b137c92cbfbbe))
* **warden:** keep the previous dead-release source if a refresh is interrupted ([#618](https://github.com/nzbdav/nzbdav/issues/618)) ([190938c](https://github.com/nzbdav/nzbdav/commit/190938ccaead6f93094146d6619f95d446f85cca))
* **webdav:** emit true GMT for getlastmodified and Last-Modified ([#558](https://github.com/nzbdav/nzbdav/issues/558)) ([31a5acc](https://github.com/nzbdav/nzbdav/commit/31a5acce776e7e5003f845b5e32fb3db726fd051))
* **webdav:** fail stuck Usenet reads quickly instead of wedging rclone ([#647](https://github.com/nzbdav/nzbdav/issues/647)) ([2cd129e](https://github.com/nzbdav/nzbdav/commit/2cd129efab5a212ec9570b40830e1bdefd85cb79))
* **webdav:** ignore malformed Range headers on GET/HEAD ([#557](https://github.com/nzbdav/nzbdav/issues/557)) ([45b7e3b](https://github.com/nzbdav/nzbdav/commit/45b7e3b531a5ee698bf8410c34eddd9961dc0a65))
* **webdav:** stop cancelled directory scans from flooding logs ([#599](https://github.com/nzbdav/nzbdav/issues/599)) ([b1722a3](https://github.com/nzbdav/nzbdav/commit/b1722a3c86263a6e579884b4575245556974510b))
* **webdav:** stop multi-part downloads playing back with silent gaps ([#641](https://github.com/nzbdav/nzbdav/issues/641)) ([4f927df](https://github.com/nzbdav/nzbdav/commit/4f927dff5d77309df98f7e05935c8a519a010235))
* **websocket:** drop oldest events for slow clients ([#604](https://github.com/nzbdav/nzbdav/issues/604)) ([f68d77f](https://github.com/nzbdav/nzbdav/commit/f68d77f5563867854d9f296b88474e0ca101b528))


### UX

* **ui:** clarify WebDAV settings with grouped layout cards ([#593](https://github.com/nzbdav/nzbdav/issues/593)) ([6d58d0b](https://github.com/nzbdav/nzbdav/commit/6d58d0bf98bdebf262e5a2a3a2c90345f21fda0a))
* **ui:** use green toggles with tooltips for boolean settings ([#653](https://github.com/nzbdav/nzbdav/issues/653)) ([b9e6fc4](https://github.com/nzbdav/nzbdav/commit/b9e6fc4969c22087fe4e4e0c2f75485efeef3c32))
* **ui:** use NzbDAV casing on pre-server-ready splash ([#592](https://github.com/nzbdav/nzbdav/issues/592)) ([f473b37](https://github.com/nzbdav/nzbdav/commit/f473b37574e8e460a0baf253d5f4498af77c0927))

## [0.8.1](https://github.com/nzbdav/nzbdav/compare/v0.8.0...v0.8.1) (2026-07-21)


### Features

* **ui:** add 35 GB and 50 GB options to the Usenet speed test ([54de9e4](https://github.com/nzbdav/nzbdav/commit/54de9e40b8b5401efb07c964d7f538bfb3d43a8e))
* **ui:** add 35 GB and 50 GB speed-test data budgets ([f65f7cd](https://github.com/nzbdav/nzbdav/commit/f65f7cdcfdfd1ed96ff74038ef5bd6279ad33d34))
* **ui:** add retry button for failed history items ([78de4b1](https://github.com/nzbdav/nzbdav/commit/78de4b158f10722da59257e0c967e6f101e2a9e3))
* **ui:** retry failed history items from the queue page ([76b2c89](https://github.com/nzbdav/nzbdav/commit/76b2c89efbc46d4a169be862e3bbce6c0ae72b26))
* **ui:** show queue totals and selectable page sizes ([906cf72](https://github.com/nzbdav/nzbdav/commit/906cf72ea012dec2a23d8b437c5ae123b8ba5ef4))
* **ui:** show queue totals and selectable page sizes on the Queue page ([8a54639](https://github.com/nzbdav/nzbdav/commit/8a546399e8a72b6354a7b796263f9dbf0ef8de0d))


### Bug Fixes

* **nntp:** tighten handshake timeout filters and Test Connection logs ([c3aae89](https://github.com/nzbdav/nzbdav/commit/c3aae89c0ec23e544134cdde65ecafacf6953aca))
* **nntp:** timed-out provider handshakes report as connect or login failures ([f5eea1e](https://github.com/nzbdav/nzbdav/commit/f5eea1ef760c956a02c0f07094244d66b62c29c7))
* **nntp:** type handshake timeouts as connect or login failures ([a9c8e2f](https://github.com/nzbdav/nzbdav/commit/a9c8e2fc54c1ac32a37172bab87bac27eb4435fd))
* **queue:** stop advertising huge sentinel sizes for multipart RAR files ([3a74b4d](https://github.com/nzbdav/nzbdav/commit/3a74b4d765ab8ebbd17479d5ef3f57374d4f0a0f))
* **queue:** stop advertising Int64.MaxValue as multipart RAR FileSize ([e5eb704](https://github.com/nzbdav/nzbdav/commit/e5eb70410a82e40160ad7c71699d500853f25dc7))
* **ui:** always show live/max on usenet provider connection tiles ([#547](https://github.com/nzbdav/nzbdav/issues/547)) ([e3cd265](https://github.com/nzbdav/nzbdav/commit/e3cd2653f187083ae10158cd5e0205accf0e7c9e))
* **ui:** Apply recommendation populates Max Connections and pipelining depth ([73a6a28](https://github.com/nzbdav/nzbdav/commit/73a6a28d28d5e541db4f736bebe159674f7188ae))
* **ui:** keep short provider circuit trips visible on Outages spark ([17720ca](https://github.com/nzbdav/nzbdav/commit/17720cadd0c1328c95fe3ae953d83bea9a87fdf5)), closes [#526](https://github.com/nzbdav/nzbdav/issues/526)
* **ui:** keep speed-test Apply recommendation from resetting the form ([dbb82d5](https://github.com/nzbdav/nzbdav/commit/dbb82d5102ca04528636b5e2b77604c16450f992))
* **ui:** Outages sparkline no longer hides brief provider circuit trips ([524dd24](https://github.com/nzbdav/nzbdav/commit/524dd247ce578820c5c3338cfcc96ff952e9db88))
* **usenet:** log yEnc CRC mismatches without stack dumps ([53fda2f](https://github.com/nzbdav/nzbdav/commit/53fda2faa281d313b7fd0fa403f5a8bc98b32333))
* **usenet:** stop dumping stacks for known yEnc CRC mismatches ([d2b2e1a](https://github.com/nzbdav/nzbdav/commit/d2b2e1a3748a0fb3e2bad6d7724fba32d338a313))
* **webdav:** heal underestimated multipart volume lengths ([57089a5](https://github.com/nzbdav/nzbdav/commit/57089a5e3405e0fa3686602688eb40772ec7fad2))
* **webdav:** prevent multipart playback failures from understated volume sizes ([3b42112](https://github.com/nzbdav/nzbdav/commit/3b421128d03e15721bb567dd8313bf7d40915c29))


### Documentation

* add since-version pills for 0.8.0 features ([075a0ac](https://github.com/nzbdav/nzbdav/commit/075a0ac9b4f0f19bd9ceca8b92d5faef1c188cdd))
* **docker:** explain how to change the published port ([7527c09](https://github.com/nzbdav/nzbdav/commit/7527c0944b66bf084f392575d490ad6b873f217a))
* **docker:** explain port mapping and listener overrides ([29b7393](https://github.com/nzbdav/nzbdav/commit/29b73931e196e4e21d5529dd679afd52f009b079))
* forbid agents from merging pull requests ([6114927](https://github.com/nzbdav/nzbdav/commit/611492746fbedd8beada84494333f1408745d24c))
* migrate Discord invite and document July 21 transition ([a955a6e](https://github.com/nzbdav/nzbdav/commit/a955a6e0b74d7f28a34ac66a6e290fb5596c450b))
* point community Discord at the new server ([20ef599](https://github.com/nzbdav/nzbdav/commit/20ef5998073e970be1a7d3c57978147630952333))
* show when features shipped with since-version pills ([3764bd9](https://github.com/nzbdav/nzbdav/commit/3764bd916f7e59090320e52b9c5520304c2ff1ec))


### Refactors

* **health:** resolve depth to an enum instead of a sentinel double ([faf441a](https://github.com/nzbdav/nzbdav/commit/faf441a28a88ed216934997f332a24107277f598))
* **health:** resolve depth to an enum instead of a sentinel double ([3ebbb01](https://github.com/nzbdav/nzbdav/commit/3ebbb016abced73f679cb0a48364858ab96135e4))

## [0.8.0](https://github.com/nzbdav/nzbdav/compare/v0.7.25...v0.8.0) (2026-07-20)


### ⚠ BREAKING CHANGES

* **db:** adds a database migration; back up /config before upgrading.

### Features

* **db:** search links keep working after restarts and have a configurable lifetime ([#452](https://github.com/nzbdav/nzbdav/issues/452)) ([f186b38](https://github.com/nzbdav/nzbdav/commit/f186b38be4338360771edfe247a2cf7198d6818e))
* gzip NZB ingest, nested RAR extraction, SAB API conformance, provider outage history, and CRC-verified downloads ([#467](https://github.com/nzbdav/nzbdav/issues/467)) ([095825d](https://github.com/nzbdav/nzbdav/commit/095825d3cc7f0464bd6b1eafa70d02bd92f3ef98))
* **health:** make the aging taper opt-in and validate the depth setting ([00c97d4](https://github.com/nzbdav/nzbdav/commit/00c97d42dda54609ebf9b0e706ab34d3d3c403ff))
* **health:** smooth out stat cliff bug, add aging function ([d73a568](https://github.com/nzbdav/nzbdav/commit/d73a568f25c505e9711f373badbc4c4498b0d1b5))
* **nntp:** allow disabling yEnc CRC validation via USENET_DISABLE_CRC_VALIDATION ([e0d10aa](https://github.com/nzbdav/nzbdav/commit/e0d10aa2605f034869885f68e9b1ac38e35dbb0e))
* **nntp:** fail fast with a logged yEnc native self-test at startup ([ec48e9d](https://github.com/nzbdav/nzbdav/commit/ec48e9d2b879c8cff3c5115f32d570acd923056c))
* **nntp:** speed up health checks and import existence probes with pipelined STAT ([#472](https://github.com/nzbdav/nzbdav/issues/472)) ([b0a93b8](https://github.com/nzbdav/nzbdav/commit/b0a93b8040c4b537b42d927d29ff9950ee8629d5)), closes [#60](https://github.com/nzbdav/nzbdav/issues/60)
* **queue:** adopt NzbDav.SharpCompress for RAR and 7z metadata parsing ([#466](https://github.com/nzbdav/nzbdav/issues/466)) ([fd1bd62](https://github.com/nzbdav/nzbdav/commit/fd1bd62e7ce87b4c66689c7ad3f7256f1cf02643))
* **queue:** allow moving queue items to the top ([#473](https://github.com/nzbdav/nzbdav/issues/473)) ([1d63c96](https://github.com/nzbdav/nzbdav/commit/1d63c96d90d98133c2f6df231a10c0127da8de39))
* **repairs:** smooth out stat cliff bug, add aging function ([6012990](https://github.com/nzbdav/nzbdav/commit/60129902894549ee4679bb94a29758bae268adda))
* **sab:** allow addurl fetches from trusted private hosts ([e7c27e7](https://github.com/nzbdav/nzbdav/commit/e7c27e7c1b06fc388b3184c0a9b817208df92fee)), closes [#433](https://github.com/nzbdav/nzbdav/issues/433)
* **sab:** allow NZB grabs from LAN indexers like Prowlarr and NZBHydra2 ([57bfe8b](https://github.com/nzbdav/nzbdav/commit/57bfe8b07c94e99ab74998ef6551a948c9aa8b4e))
* **ui:** reset Overview statistics (all or per provider) from Maintenance settings ([#444](https://github.com/nzbdav/nzbdav/issues/444)) ([97e1dfe](https://github.com/nzbdav/nzbdav/commit/97e1dfe8693e331f858a1be99b0bf1b54cf5a6d5))
* **ui:** show client identity on Active Reads ([#469](https://github.com/nzbdav/nzbdav/issues/469)) ([4fdb772](https://github.com/nzbdav/nzbdav/commit/4fdb77258da0b3e521e86e224633ffa2d64e2c4f))


### Bug Fixes

* **ci:** restore issue forms by removing empty title fields ([#475](https://github.com/nzbdav/nzbdav/issues/475)) ([2bd2305](https://github.com/nzbdav/nzbdav/commit/2bd2305574d965f17821f8682f08ac9c8030cab6))
* **deps:** bump NzbDav.SharpCompress to 0.53.1 with bounded 7z recursion ([bf4b436](https://github.com/nzbdav/nzbdav/commit/bf4b43606e00d8a9d7dbf426fa0e70adae3fbeb8))
* **deps:** bump NzbDav.UsenetSharp to 3.1.3 for musl rapidyenc ([f009c9b](https://github.com/nzbdav/nzbdav/commit/f009c9b6479169f3dc5f32f1fe9813bc719fd1dc))
* **deps:** bump SharpCompress so deep 7z coder chains fail safely ([cb71c15](https://github.com/nzbdav/nzbdav/commit/cb71c151e2171f404176ab7b206eb83c3a97c9b2))
* **deps:** bump UsenetSharp so Alpine images get musl-native yEnc decode ([eeff02b](https://github.com/nzbdav/nzbdav/commit/eeff02bcf6e510739142b8bddd8bbb2c2c0426a0))
* **deps:** resolve merge keeping UsenetSharp 3.1.3 and SharpCompress 0.53.1 ([d16405d](https://github.com/nzbdav/nzbdav/commit/d16405d0a1652ee4a78a678f9527fdc1cd99ba4e))
* **docker:** container shutdown logs the backend exit code and fatal signal ([49a6473](https://github.com/nzbdav/nzbdav/commit/49a647325d8fb540536f2ad37d5ea2c09ade516f))
* **docker:** log the backend exit code and fatal signal when the container shuts down ([edee229](https://github.com/nzbdav/nzbdav/commit/edee229fe6639a2907f8df37663c4bda28697aa9))
* **health:** log NNTP transport timeouts as human-readable warnings ([#481](https://github.com/nzbdav/nzbdav/issues/481)) ([2e83a8c](https://github.com/nzbdav/nzbdav/commit/2e83a8c6b7e611b145e8a76e79d9a02092f28808))
* **health:** resolve the depth setting regardless of casing ([742ea97](https://github.com/nzbdav/nzbdav/commit/742ea97b9eea0b64aa21811e178cdbeca307abbd))
* **nntp:** connection permit release no longer throws from download callbacks ([95b18c0](https://github.com/nzbdav/nzbdav/commit/95b18c0bbfeb4fb0a4e0890849d70bf2cc44cd08))
* **nntp:** fail fast on broken yEnc natives and catch Alpine decode crashes in CI ([c9c42eb](https://github.com/nzbdav/nzbdav/commit/c9c42eb5801a6f59ba271faa96709c3e50728d8c))
* **nntp:** health checks and import probes stay fast when NNTP pipelining is on ([2b7d462](https://github.com/nzbdav/nzbdav/commit/2b7d462e30d27bb4a849dfe5a7ce580547e8cb86))
* **nntp:** health checks no longer fail when a pipelined STAT session dies mid-sweep ([#476](https://github.com/nzbdav/nzbdav/issues/476)) ([97db88a](https://github.com/nzbdav/nzbdav/commit/97db88a2523024326d24c51c221939add38f788d))
* **nntp:** keep health and import existence checks on concurrent STAT ([91704be](https://github.com/nzbdav/nzbdav/commit/91704bec7033a3b20b298d00276a65b66ea0e72d))
* **nntp:** make connection-permit release safe inside completion callbacks ([a691499](https://github.com/nzbdav/nzbdav/commit/a6914994b537b7b47621ee91daff8b54d3ccd643))
* **nntp:** only log provider recovery when the circuit actually opened ([5b33db1](https://github.com/nzbdav/nzbdav/commit/5b33db18bc82b34b023dfdba3b66efbca52f572c))
* **nntp:** only log provider recovery when the circuit actually opened ([48e79d7](https://github.com/nzbdav/nzbdav/commit/48e79d7f8919a533b52472e6eb5283c30622c06f))
* **nntp:** stop Progress wrappers racing pipelined STAT fallback reports ([#483](https://github.com/nzbdav/nzbdav/issues/483)) ([75c02cd](https://github.com/nzbdav/nzbdav/commit/75c02cd365aa68cdf7d4a16fc2643aee587945aa))
* **nntp:** stop providers getting stuck in the probing state ([6150de7](https://github.com/nzbdav/nzbdav/commit/6150de71e2ced9b6f394910f25d1ec659206a23a))
* **nntp:** stop providers getting stuck in the probing state ([0de9ace](https://github.com/nzbdav/nzbdav/commit/0de9aced5368e81e3ac9c65dea7d968643eef366))
* **queue:** import obfuscated multi-volume RAR sets with duplicate subjects or incomplete volumes ([#471](https://github.com/nzbdav/nzbdav/issues/471)) ([4148813](https://github.com/nzbdav/nzbdav/commit/41488135b809802511eae8beae2b193c097b3fa3))
* **queue:** progress broadcasts can no longer crash the backend from a timer callback ([016f6e0](https://github.com/nzbdav/nzbdav/commit/016f6e04c99d7fb1a24cd0703c0c8465c0aad494))
* **queue:** queue processing starts only after the web host is healthy ([015ffdd](https://github.com/nzbdav/nzbdav/commit/015ffdddc27465e5de84ffcbb9dce41702020fbd))
* **queue:** queue progress updates no longer crash the backend on a bad callback ([9fc3841](https://github.com/nzbdav/nzbdav/commit/9fc384167f9ad3eebdc17ce534298c2a43b2f13e))
* **queue:** removing a blocklisted file no longer crashes the queue ([#446](https://github.com/nzbdav/nzbdav/issues/446)) ([97f0ebe](https://github.com/nzbdav/nzbdav/commit/97f0ebe63acf66f89dbfd30e2dfce5e81148e254))
* **queue:** start queue processing only after the web host is serving ([8bd9240](https://github.com/nzbdav/nzbdav/commit/8bd924047b09e0119da72633ab0e0e4cc61e7ba5))
* **ui:** keep live updates connected across backend websocket relay drops ([9f8e809](https://github.com/nzbdav/nzbdav/commit/9f8e809b312d3d4cfd03f2af090e5d6df2334811)), closes [#515](https://github.com/nzbdav/nzbdav/issues/515)
* **ui:** live Overview and Queue updates no longer stall until refresh ([4499595](https://github.com/nzbdav/nzbdav/commit/44995954d66e74fb502baab7a0ac08c3e2657ca1))
* **ui:** remove MaxListenersExceededWarning noise from container logs ([2b0afac](https://github.com/nzbdav/nzbdav/commit/2b0afac782bb683fba93115dce33cba823ae6260))
* **ui:** stop stacking proxy timeout listeners on keep-alive sockets ([6fa0dda](https://github.com/nzbdav/nzbdav/commit/6fa0dda7b3c7715d2b60495951143d24b2ca8f00))
* **ui:** stop the read sessions panel overflowing at full width ([ddd36be](https://github.com/nzbdav/nzbdav/commit/ddd36be9f6617ba2ee78a67ddf1c9e03e1ae8143))
* **ui:** stop the read sessions panel overflowing at full width ([dfcfef0](https://github.com/nzbdav/nzbdav/commit/dfcfef042490274eb2214cd69a16f8d57d42ebc0))
* **webdav:** clamp a /view range end past the file so response headers stay valid ([#447](https://github.com/nzbdav/nzbdav/issues/447)) ([92bdd81](https://github.com/nzbdav/nzbdav/commit/92bdd819a2d4f5711a9426053b28efef680b6d83))
* **webdav:** treat corrupt Lazy RAR as permanent miss and schedule repair ([#484](https://github.com/nzbdav/nzbdav/issues/484)) ([318b8f7](https://github.com/nzbdav/nzbdav/commit/318b8f7db87c8473cc662488f58b4ab6b2ae9a45))


### Performance Improvements

* **queue:** cap import pipelining depth to bound first-segment memory ([b690fa8](https://github.com/nzbdav/nzbdav/commit/b690fa8508094a4fe0d3b6691dd9f53a7c524645))
* **queue:** limit first-segment import pipelining to reduce memory spikes ([42f0273](https://github.com/nzbdav/nzbdav/commit/42f02737458459fb5850324613610462afc03c8c))


### Documentation

* add migration paths from nzbdav-dev and community forks ([96ff4c9](https://github.com/nzbdav/nzbdav/commit/96ff4c91f25f1a67cfce8a76003125f43f5b2179))
* align Effort scale with Size (XS–XL) ([87ea0b9](https://github.com/nzbdav/nzbdav/commit/87ea0b92b92401c78f6eb359dd75b566139af6a2))
* clarify homepage legal-use disclaimer for public domain content ([1edf4c4](https://github.com/nzbdav/nzbdav/commit/1edf4c4d67b7a5f2bc6b700f784af0999f9a5ff9))
* document current issue triage and milestone practice ([25546bd](https://github.com/nzbdav/nzbdav/commit/25546bde44eee6f03d78310359d0651f2e282992))
* document current issue triage and milestone practice ([568b471](https://github.com/nzbdav/nzbdav/commit/568b4719ee58a9e17710c1cd4ef51cb5e501c2a3))
* document latest vs lts Docker image tags in README ([#458](https://github.com/nzbdav/nzbdav/issues/458)) ([46a62ec](https://github.com/nzbdav/nzbdav/commit/46a62ec81fc313867edd73c5fa2e73394345adec))
* feature Discord invite in community and footer socials ([258ad3f](https://github.com/nzbdav/nzbdav/commit/258ad3f53f594e65ceca33ac655e1e59005d44ed))
* hide Made with Zensical footer credit ([13c6a7b](https://github.com/nzbdav/nzbdav/commit/13c6a7b36a4e949bb6bf09a0d5cf0ed7c25b0324))
* note NzbDAV as a fully supported DUMB core module ([776f10e](https://github.com/nzbdav/nzbdav/commit/776f10edf7a579e9e5c9df7aaf7f22bf0ad73e7a))
* note same-origin /ws WebSocket requirement for reverse proxies ([a4168e7](https://github.com/nzbdav/nzbdav/commit/a4168e75d70540ded5dcd5c56b6dd6f05e5279c0))
* rebuild nzbdav.com as a branded product docs site ([2d1a6f7](https://github.com/nzbdav/nzbdav/commit/2d1a6f703b4ae694fb15a1552ccc684ec3e385b2))
* rebuild nzbdav.com site with branded guides and settings reference ([64a215d](https://github.com/nzbdav/nzbdav/commit/64a215d5cbffbd8cfd4817db9a2b5c5f2f1579e5))
* require breaking commits for migrations and breaking changes ([de96b56](https://github.com/nzbdav/nzbdav/commit/de96b563ed64ff37a9ada498e7298e658fd5d822))
* require human-friendly log events for stack dumps ([9b69c8f](https://github.com/nzbdav/nzbdav/commit/9b69c8f20b8f0d7a9cb48aab96ccb2d18955ed19))
* **ui:** shrink homepage hero screenshot so it dominates less ([ddcc7c4](https://github.com/nzbdav/nzbdav/commit/ddcc7c48bedc059370fa2f430f3d961420a2c7d6))
* **ui:** use product logo in header and mid-size the hero screenshot ([5fec824](https://github.com/nzbdav/nzbdav/commit/5fec8241896c408ddfd665dc1bb240d6e9efe7d0))
* **ui:** use README product screenshot as the homepage hero ([37bdb9d](https://github.com/nzbdav/nzbdav/commit/37bdb9d4dfe4d9c9f52b2b13846d23f188376997))
* use Issue Priority/Effort fields instead of labels ([a04f392](https://github.com/nzbdav/nzbdav/commit/a04f39204ad73a16bbfc6632dbb749f9ab808850))
* use NzbDAV casing, add alternatives comparison, shrink badges ([c2db122](https://github.com/nzbdav/nzbdav/commit/c2db122840990f360f86b1fb4aec40a08867b364))
* use NZBDav Ecosystem project fields for Priority and Effort ([a5786ba](https://github.com/nzbdav/nzbdav/commit/a5786ba97a226140a4944b87ef5ef3362e61b8df))

## [0.7.25](https://github.com/nzbdav/nzbdav/compare/v0.7.24...v0.7.25) (2026-07-17)


### Features

* **ui:** modernize speed-test panel and raise data budget options ([853c4cd](https://github.com/nzbdav/nzbdav/commit/853c4cdab24dc5f7f81afc740803a61804d04b5c))


### Bug Fixes

* **usenet:** keep long speed tests alive past proxy timeouts ([aeca1cf](https://github.com/nzbdav/nzbdav/commit/aeca1cfa07638ec06b86ad20a37c01de5a7a8049))
* **usenet:** prefer healthy large files for speed-test corpus ([745a82a](https://github.com/nzbdav/nzbdav/commit/745a82ad335ac03fbd669046f39617388eb07171))
* **usenet:** recover speed-test rates when the byte budget finishes in warmup ([ef4ff24](https://github.com/nzbdav/nzbdav/commit/ef4ff24a9ad5549e9be00d690cfb0b5f36858881))
* **usenet:** rename speed-test MbPerSec fields to MegaBytesPerSec ([643d278](https://github.com/nzbdav/nzbdav/commit/643d27863da9ba1451d6272360d97d0a922d726f))
* **usenet:** reserve speed-test budget for pipelining recommendations ([7ca4c0b](https://github.com/nzbdav/nzbdav/commit/7ca4c0b564f2d00df4bc49c2b97364fa63e086b1))
* **usenet:** score speed-test confidence from knee region and confirm runs ([7445ef0](https://github.com/nzbdav/nzbdav/commit/7445ef07ad09f781aaa8ff7fa1ef254fcc33c21f))
* **usenet:** speed test keeps budget for pipelining and fits results in the provider modal ([1d32148](https://github.com/nzbdav/nzbdav/commit/1d321487092c3f6493e1fe03191e3a91d1e70282))
* **usenet:** speed test no longer reports low confidence on fast connections ([b66f375](https://github.com/nzbdav/nzbdav/commit/b66f375530e457d2e6ad08dfca449682f8d3f27b))
* **usenet:** speed test uses MB correctly and survives long 20 GB runs ([69bf34e](https://github.com/nzbdav/nzbdav/commit/69bf34e9f6c5bfe2e176ace298c048a5499bcdfd))
* **usenet:** stop speed-test cancel from poisoning NNTP sockets ([ede637e](https://github.com/nzbdav/nzbdav/commit/ede637e191f1ade24a10e42c711771b85b815d03))
* **usenet:** stop verify-at-N from reporting 0 MB/s on fast lines ([0ed20cb](https://github.com/nzbdav/nzbdav/commit/0ed20cbbd07c54b2818e6ca7f6f9f60a02386da2))
* **usenet:** Verify at N connections no longer reports 0 MB/s on fast lines ([62a94c9](https://github.com/nzbdav/nzbdav/commit/62a94c9b52c3b2770955ae2eba1605ef03b843ed))


### UX

* **usenet:** clarify speed-test rates vs total data used ([#440](https://github.com/nzbdav/nzbdav/issues/440)) ([a954e4d](https://github.com/nzbdav/nzbdav/commit/a954e4d446fbd26a23b18ac8cf3e627c7bea3a4f))

## [0.7.24](https://github.com/nzbdav/nzbdav/compare/v0.7.23...v0.7.24) (2026-07-17)


### Bug Fixes

* **deps:** bump the github-actions group across 1 directory with 2 updates ([5290539](https://github.com/nzbdav/nzbdav/commit/5290539c65da4ab9c30abd23653d10f8cac90afd))
* **deps:** bump the github-actions group across 1 directory with 2 updates ([c3bf15d](https://github.com/nzbdav/nzbdav/commit/c3bf15d18ead06776becd31aef0a3c17fc29d578))
* **queue:** abort first-segment checks early when an important file is missing ([63404fa](https://github.com/nzbdav/nzbdav/commit/63404fa71a64c750bd04d6a92ad57039d52174fa))
* **queue:** fail dead NZBs as soon as the first missing RAR is confirmed ([4288fc1](https://github.com/nzbdav/nzbdav/commit/4288fc16e70f359de2bc5d0579a0ef31c8d28a7e))
* **sab:** replace existing queue item on addfile name collision ([b3ab0fb](https://github.com/nzbdav/nzbdav/commit/b3ab0fb835de17c03c36c6b7a6d6bcc4ff837749))
* **sab:** Sonarr re-adds no longer fail when the previous NZB is still in the queue ([1e98dde](https://github.com/nzbdav/nzbdav/commit/1e98dde58c6e8ea94275c50ad7119ee165d75ee6))


### CI/CD Pipeline

* keep the git `dev` tag in sync with the `dev` container image ([c26c774](https://github.com/nzbdav/nzbdav/commit/c26c774f315ac343801afd870216398a05138960))
* move git dev tag with pre-release and release image publishes ([5a51cef](https://github.com/nzbdav/nzbdav/commit/5a51cef879f5ddc54837c8795db2267a01de59a8))

## [0.7.23](https://github.com/nzbdav/nzbdav/compare/v0.7.22...v0.7.23) (2026-07-17)


### Bug Fixes

* **deps:** bump zensical from 0.0.47 to 0.0.50 in the docs-python group ([f4dc57f](https://github.com/nzbdav/nzbdav/commit/f4dc57f2ea98c70adec8727981d2a0c3bcc16841))
* **deps:** bump zensical from 0.0.47 to 0.0.50 in the docs-python group ([c75ef53](https://github.com/nzbdav/nzbdav/commit/c75ef53e17ac46054dd18efd8f82c2aedab78592))
* **nntp:** fail DMCA'd NZBs faster when NNTP pipelining is enabled ([d081b0c](https://github.com/nzbdav/nzbdav/commit/d081b0cd202fa1449c6a90450222709e2f9532d8))
* **nntp:** skip rescue re-verification for definitively missing pipelined articles ([0e18d7d](https://github.com/nzbdav/nzbdav/commit/0e18d7d7f45a308cbf91adf94248e036dd37f16b))
* **queue:** keep Remove Orphaned Files elapsed timer visible ([51ff5d5](https://github.com/nzbdav/nzbdav/commit/51ff5d53e8cd012b9228da006a82c2693696d5a6))
* **queue:** keep Remove Orphaned Files progress updating during quiet phases ([8cc4fa7](https://github.com/nzbdav/nzbdav/commit/8cc4fa7cc8dfcb75207280a8409593e5842bef70))
* **queue:** queue no longer waits a full minute before retrying after provider errors ([3c3c7cf](https://github.com/nzbdav/nzbdav/commit/3c3c7cf655731a360a96dfe39416e31478a0501a))
* **queue:** remember missing first segments so retries and re-grabs fail fast ([1b11756](https://github.com/nzbdav/nzbdav/commit/1b11756fab2ddb80df2ebd96e65107c8a3679314))
* **queue:** Remove Orphaned Files elapsed timer no longer flashes ([3387d78](https://github.com/nzbdav/nzbdav/commit/3387d785c3366df6628b55277dd85e03618c0b0a))
* **queue:** Remove Orphaned Files no longer looks frozen mid-scan ([0ec15f7](https://github.com/nzbdav/nzbdav/commit/0ec15f7a72261f102938f184ca5c01a7fd9499b6))
* **queue:** Remove Orphaned Files no longer scans the linked-id table per row ([eec6ea8](https://github.com/nzbdav/nzbdav/commit/eec6ea87ea507b21f4e65ef968e8b587247746d3))
* **queue:** restore Remove Orphaned Files linked-id index seeks ([92ee261](https://github.com/nzbdav/nzbdav/commit/92ee2610080342ffc18299b75b690dea50e57707))
* **queue:** wake queue when a retry pause expires instead of sleeping a full minute ([6edcf31](https://github.com/nzbdav/nzbdav/commit/6edcf31a51308dd625fcb1d303bd46575d8fdc75))
* **usenet:** produce stable speed test recommendations ([55d7b1c](https://github.com/nzbdav/nzbdav/commit/55d7b1c7b4877bf4e415c1368b347494c2742fa8))
* **usenet:** stabilize speed test recommendations ([65ef8a8](https://github.com/nzbdav/nzbdav/commit/65ef8a8801383b59a33d04a34921c71e273a2bb2))
* **webdav:** keep encrypted archive playback running when parts end early ([b007036](https://github.com/nzbdav/nzbdav/commit/b00703648f82359b018bfbe9e41668828b9204c5))
* **webdav:** preserve offsets when encrypted parts end early ([be17df5](https://github.com/nzbdav/nzbdav/commit/be17df5b0f227714f49b25eabb910ee42dd93b5e))


### Chores

* **ci:** update dev image tag on releases ([d5267ba](https://github.com/nzbdav/nzbdav/commit/d5267bad48be31582e8f1772b1892dc3d54e55e4))
* **docs:** expand release-please changelog sections and commit types ([ae52ea2](https://github.com/nzbdav/nzbdav/commit/ae52ea203be09de007469503f90546c5c19e80d9))
* **docs:** expand release-please changelog sections and commit types ([66edecc](https://github.com/nzbdav/nzbdav/commit/66edecc80fd84e3267745256c213cebf2d78a632))
* update release-please config ([eb945a5](https://github.com/nzbdav/nzbdav/commit/eb945a5bad2153beabb911e8941da97681da1bef))


### UX

* **ui:** modernize maintenance, backup, and usenet settings pages ([d64f15d](https://github.com/nzbdav/nzbdav/commit/d64f15dd2b405d3c0469b85fa75afe9bc8da7844))

## [0.7.22](https://github.com/nzbdav/nzbdav/compare/v0.7.21...v0.7.22) (2026-07-16)


### Features

* **api:** add opt-in stream trace buffer with dump endpoints ([2994423](https://github.com/nzbdav/nzbdav/commit/2994423199a8b937615d8a618f278824953a1f66))
* **api:** opt-in playback stream tracing for debugging seek and zero-fill issues ([2a8d1a0](https://github.com/nzbdav/nzbdav/commit/2a8d1a04e1fe62c0f2fa2ec05014b4cb7990aa37))
* **docs:** add Zensical site config and GitHub Pages workflow ([a1b7c07](https://github.com/nzbdav/nzbdav/commit/a1b7c07f55049754f5755319035046058cfafb29))
* **docs:** publish project documentation with Zensical on GitHub Pages ([81f28c5](https://github.com/nzbdav/nzbdav/commit/81f28c55b4402d64f2f1460c56f2125db49e6dc2))
* **nntp:** emit segment, failover, seek, and zero-fill trace events ([31ab618](https://github.com/nzbdav/nzbdav/commit/31ab618056db10d734f0a8554feee0217c6b4411))
* **ui:** modernize health schedule table and overview chrome ([d26ebf4](https://github.com/nzbdav/nzbdav/commit/d26ebf4e358b3b3dbc66aaf7198cd1d8a485c1c8))
* **ui:** modernize the health schedule table ([397cdfe](https://github.com/nzbdav/nzbdav/commit/397cdfe6c27dafd20c7960e18106dd1ba1237f06))
* **ui:** restore Overview activity chart hover tooltip and sparse errors ([04b1ae8](https://github.com/nzbdav/nzbdav/commit/04b1ae896d6cdd4e8ea88f2145891c0abf97f2d0))
* **ui:** show copyable session id on live reads panel ([5807c4d](https://github.com/nzbdav/nzbdav/commit/5807c4d059059b6c2f3aae799dab2f4632a3fa13))
* **ui:** show error trends per provider on the overview scoreboard ([69fd8be](https://github.com/nzbdav/nzbdav/commit/69fd8be57dfc156e8c6feb7e643ba07e604b66ed))
* **ui:** show per-provider error sparkline on overview ([b789d08](https://github.com/nzbdav/nzbdav/commit/b789d08e8b76c82aae15c708bf3183391fa95bc0))
* **ui:** show per-provider retry sparkline on overview ([15f88cd](https://github.com/nzbdav/nzbdav/commit/15f88cd6ab57452574cde9e05076d3a81d443da3))
* **ui:** show provider download speed on the activity chart ([78980bc](https://github.com/nzbdav/nzbdav/commit/78980bcc69e0dba50b4b84f4b0274ad4627ae138))
* **ui:** show provider download throughput on the activity chart ([9c148f5](https://github.com/nzbdav/nzbdav/commit/9c148f5a64b74bc5786ccb8e7fa076e0bdb93175))
* **ui:** show retry trends per provider on the overview scoreboard ([a37708e](https://github.com/nzbdav/nzbdav/commit/a37708ea0418ebd82dd665dd4507ae4a5b4c874a))
* **webdav:** trace range lifecycle and enrich terminal read sessions ([86a60c7](https://github.com/nzbdav/nzbdav/commit/86a60c7b2c827395656dd097cbe7b51b07ff342d))


### Bug Fixes

* **api:** backup download no longer fails with a browser network error ([d4efd8a](https://github.com/nzbdav/nzbdav/commit/d4efd8ac7cd11a9910365c4f73ba60b5ff47b645))
* **api:** stream backup downloads without Kestrel sync-I/O abort ([116fa75](https://github.com/nzbdav/nzbdav/commit/116fa75fc9e74bb07b2bfc78068c7cc3db9c1f31))
* **config:** reject control characters in Usenet provider Host/User/Pass ([5dab024](https://github.com/nzbdav/nzbdav/commit/5dab024db93e67f7eedd2aa7ffb01859a18009b4)), closes [#392](https://github.com/nzbdav/nzbdav/issues/392)
* **db:** database upgrade no longer stalls on the Metrics database step ([011f43f](https://github.com/nzbdav/nzbdav/commit/011f43f96dca8ad4800117bca7df9b24472c494b))
* **db:** prevent metrics migration startup stalls ([b0a038f](https://github.com/nzbdav/nzbdav/commit/b0a038f78af02a5a6ab5f8e49ee75f878e1cb68f))
* **nntp:** fail non-yEnc size probes with a clear NonRetryable error ([5b1be7a](https://github.com/nzbdav/nzbdav/commit/5b1be7a87cad8d42a78a895a4cbec7e5e12157f3)), closes [#395](https://github.com/nzbdav/nzbdav/issues/395)
* **nntp:** harden STAT classification, auth, and provider validation from protocol audit ([96277d5](https://github.com/nzbdav/nzbdav/commit/96277d5badf9ec57c409b75f59af0f1e2974cf0b))
* **nntp:** seeking during playback no longer falsely trips the provider circuit breaker ([eb06cdd](https://github.com/nzbdav/nzbdav/commit/eb06cddabec36ea61a07c6234bd8368017ff9123))
* **nntp:** skip AUTHINFO when provider credentials are empty ([497d4a9](https://github.com/nzbdav/nzbdav/commit/497d4a984b4686c2fa2a75824a2fcb91c596b402)), closes [#391](https://github.com/nzbdav/nzbdav/issues/391)
* **nntp:** skip circuit breaker on seek-abort NotRetrieved ([be652c1](https://github.com/nzbdav/nzbdav/commit/be652c17a63feb317b05360932606e3eb0610b1d))
* **nntp:** treat connection-level STAT codes as retryable, not article verdicts ([66aded6](https://github.com/nzbdav/nzbdav/commit/66aded6b5aca976ab07717bd2aecec1502d73b2c)), closes [#390](https://github.com/nzbdav/nzbdav/issues/390)
* **nntp:** warn when provider credentials are used without TLS ([1ac5005](https://github.com/nzbdav/nzbdav/commit/1ac500516d29a524a743db70142189bd1b4753eb)), closes [#394](https://github.com/nzbdav/nzbdav/issues/394)
* **queue:** honor PAR2 async enumerator cancellation ([b1fd594](https://github.com/nzbdav/nzbdav/commit/b1fd5940bb8fa52f04145647b05aea4f25ee3e2d))
* **queue:** stop PAR2 scans when enumeration is cancelled ([d2e7a20](https://github.com/nzbdav/nzbdav/commit/d2e7a20d66f0688fffe5fecce4112dfd4ac0863c))
* **ui:** detect updates for main-&lt;sha&gt; builds via version-embedded SHA ([96291bd](https://github.com/nzbdav/nzbdav/commit/96291bd7ab7447ad8518b92c5676b36d939dcced))
* **ui:** detect updates for main-&lt;sha&gt; builds via version-embedded SHA ([0a52085](https://github.com/nzbdav/nzbdav/commit/0a5208591678e68a20593dfddba4eab5dba99435))
* **ui:** fold download rate into the activity articles legend ([def8004](https://github.com/nzbdav/nzbdav/commit/def8004dcd2284ea59d2115fd1e4b809ac1b7484))
* **ui:** give Overview live stats an elevated border and surface ([f65ba62](https://github.com/nzbdav/nzbdav/commit/f65ba62f4c4b6483f4da7de48f0a459784abe126))
* **ui:** Overview live-stat row is visible against the page background ([1ea11fc](https://github.com/nzbdav/nzbdav/commit/1ea11fc7baa0e26dad8a91cdadde97c34978a991))
* **ui:** restore Overview heatmap week mode class for typecheck ([e0d41e7](https://github.com/nzbdav/nzbdav/commit/e0d41e7f44bfa1891d54459c5fe3f4e9fc0f1930))
* **ui:** show download speed on the activity articles legend ([1e536a4](https://github.com/nzbdav/nzbdav/commit/1e536a41af1c94213d6fc30f38c4a482327e11c2))
* **webdav:** abort incomplete streaming responses ([67b4272](https://github.com/nzbdav/nzbdav/commit/67b4272ec00348a4e3af475ea599a3d8e0dc27fd))
* **webdav:** remove spurious Content-Length mismatch errors when playback hits missing articles ([4c57ef1](https://github.com/nzbdav/nzbdav/commit/4c57ef1d4b6260ff37de70065a481f5dc35d2c4c))
* **webdav:** stop broken files from flooding logs and Usenet traffic ([7331053](https://github.com/nzbdav/nzbdav/commit/73310530879d24454bde8bf7331897f2bb01b100))
* **webdav:** stop repeated zero-fill fetch storms ([b90ac0f](https://github.com/nzbdav/nzbdav/commit/b90ac0f84463e8bfe8cbfa26d05c5f514f7dea75))

## [0.7.21](https://github.com/nzbdav/nzbdav/compare/v0.7.20...v0.7.21) (2026-07-15)


### Features

* **ui:** show provider circuit breaker status on overview ([73366e9](https://github.com/nzbdav/nzbdav/commit/73366e935d9e2ddec807506178e2f81299433a4a))
* **ui:** show provider circuit breaker status on overview ([b430a3b](https://github.com/nzbdav/nzbdav/commit/b430a3bd07c3624951f33cee1e96fc8e5f5dde06)), closes [#162](https://github.com/nzbdav/nzbdav/issues/162)


### Bug Fixes

* **auth:** raise password verification cache size for Basic Auth retry bursts ([d5d0e4d](https://github.com/nzbdav/nzbdav/commit/d5d0e4d7b0423fb0791d367181421992bf6f8700))
* **auth:** raise password verification cache size for Basic Auth retry bursts ([8a6fc1c](https://github.com/nzbdav/nzbdav/commit/8a6fc1ca4641e6083ee2a3ecd2f9bcc230f399e9)), closes [#162](https://github.com/nzbdav/nzbdav/issues/162)
* drain WithConcurrencyAsync running tasks on early exit ([a9c276b](https://github.com/nzbdav/nzbdav/commit/a9c276bd8d371288996979db07e2177c1dbec06d))
* drain WithConcurrencyAsync running tasks on early exit ([319c656](https://github.com/nzbdav/nzbdav/commit/319c656c5b0362e0de4c76d8619b724195ff70e6))
* **usenet:** fix boot-loop timeout for stats data migrations for incoming nzbdavex users ([e0eef52](https://github.com/nzbdav/nzbdav/commit/e0eef52039b821a6b41345db2cdade1379811788))
* **usenet:** move legacy metrics remap off the blocking startup path ([f0101c7](https://github.com/nzbdav/nzbdav/commit/f0101c7fafa76d3b789b4f75a1db2c05c317e071))

## [0.7.20](https://github.com/nzbdav/nzbdav/compare/v0.7.19...v0.7.20) (2026-07-15)


### Bug Fixes

* **auth:** use fixed-time comparison for websocket API key auth ([5a94f18](https://github.com/nzbdav/nzbdav/commit/5a94f18482e5ddd1b37a4892cfe847a3400c1045))
* **config:** clamp streaming-priority and harden numeric getters ([43cc7d0](https://github.com/nzbdav/nzbdav/commit/43cc7d04bde7c3d5ee9704533304537993e4a248))
* **config:** validate usenet providers to prevent MaxConnections boot-loop ([5cabda4](https://github.com/nzbdav/nzbdav/commit/5cabda4339a4e3987f87735e95e212b1acd850d3))
* **metrics:** do not record STAT/HEAD/DATE successes as Missing ([4a004ce](https://github.com/nzbdav/nzbdav/commit/4a004ce11296df07b83742bc1d58248bfbc67231))
* **nntp:** wake queued waiters when PrioritizedSemaphore max increases ([5d486ef](https://github.com/nzbdav/nzbdav/commit/5d486ef837b5d8a9752ec60f8f3fc7e3114038a3))
* **queue:** back off on persistent loop errors and honor shutdown idle ([7df775e](https://github.com/nzbdav/nzbdav/commit/7df775e788e3742113b412a61b7602129fd6e9a4))
* **queue:** cap ArticleCachingNntpClient cache-dir delete retries ([8405e1f](https://github.com/nzbdav/nzbdav/commit/8405e1f9f3b0d2eb19b1cb2aeefc5050c2401b1b))
* **queue:** harden Remove Orphaned Files empty-dir sweep ([37d5766](https://github.com/nzbdav/nzbdav/commit/37d57667c3bddc3ff788fafd4d2546f7d54c85ba))
* **queue:** harden Remove Orphaned Files empty-dir sweep ([43994a5](https://github.com/nzbdav/nzbdav/commit/43994a59d7d9996314653e14e1cefe030e2e9c79))

## [0.7.19](https://github.com/nzbdav/nzbdav/compare/v0.7.18...v0.7.19) (2026-07-15)


### Features

* **api:** retention pruning for on-disk nzb backups ([ecf064a](https://github.com/nzbdav/nzbdav/commit/ecf064a409672acb173066c4ff5a8f23005135c4))
* **health:** auto-remove files after repeated streaming failures ([5f6c2a3](https://github.com/nzbdav/nzbdav/commit/5f6c2a31c125f611d0507dc5029a3847496f0fb3))
* **health:** auto-remove files after repeated streaming failures ([23f9479](https://github.com/nzbdav/nzbdav/commit/23f9479059cdf8f3b05c38080bbaac9145225705))
* **health:** deletion audit log for DavItem removals ([b5efa7d](https://github.com/nzbdav/nzbdav/commit/b5efa7d0bbf15cf12268cd55c6645c44dcb81049))
* **health:** structured audit log for all dav item deletions ([470f99d](https://github.com/nzbdav/nzbdav/commit/470f99defe8bfa84bf2b28123af4648999cdaadf))
* **nntp:** configurable idle connection timeout ([efb6fca](https://github.com/nzbdav/nzbdav/commit/efb6fcaddd1db40816f141e38d24809a2e2f25fa))
* **nntp:** idle timeout and range prefetch cap ([#59](https://github.com/nzbdav/nzbdav/issues/59)) ([8155e54](https://github.com/nzbdav/nzbdav/commit/8155e5402ee7a2663a330a835ba3a95974c9fe8e))
* **queue:** blocklist unpack decoy files by default ([c563a4e](https://github.com/nzbdav/nzbdav/commit/c563a4e4150a3cdcc26a326cb72890358ed58db8))
* **queue:** prefer PAR2 UniFileN unicode filenames when present ([8d43c8f](https://github.com/nzbdav/nzbdav/commit/8d43c8fd3f8d770494c585d7c92273afd75d0351))
* **queue:** prefer PAR2 UniFileN unicode filenames when present ([a475d1e](https://github.com/nzbdav/nzbdav/commit/a475d1eccb58f355ed087116fc01209efcd94b17))
* **queue:** recreate-strm-files maintenance task ([6930113](https://github.com/nzbdav/nzbdav/commit/69301134629ed459d562bb34aa8bec6ad4302d00))
* **queue:** recreate-strm-files maintenance task ([d0e1763](https://github.com/nzbdav/nzbdav/commit/d0e176336765d86e1187fefec04b7b9e47ee3b5f))
* **queue:** setting to fail jobs when non-video files have missing articles ([a735293](https://github.com/nzbdav/nzbdav/commit/a73529317172bf073465e5621b26963d0aad96fe))
* **queue:** setting to fail jobs when non-video files have missing articles ([ddceddb](https://github.com/nzbdav/nzbdav/commit/ddceddb921099915e485c5e58b5ade5794c5236a))
* **queue:** try duplicate nzb segment message-ids as ordered fallbacks ([dc264b7](https://github.com/nzbdav/nzbdav/commit/dc264b786dbc711b5d2b6382528ab2fe49804dd5))
* **queue:** try duplicate nzb segment message-ids as ordered fallbacks ([a55d85c](https://github.com/nzbdav/nzbdav/commit/a55d85c5130498e8c5a75fe6af0d8ed7faabb813))
* **webdav:** maintenance task to rename windows-invalid dav paths ([7a01d8c](https://github.com/nzbdav/nzbdav/commit/7a01d8c79c435ccf63ae5cf58305527a84354a0f))
* **webdav:** maintenance task to rename windows-invalid dav paths ([a7e839d](https://github.com/nzbdav/nzbdav/commit/a7e839dd88fab4b6091f078df6a8ca9f444a23aa))
* **webdav:** per-segment streaming timeout with fast failover ([7ba0682](https://github.com/nzbdav/nzbdav/commit/7ba0682e6094548f7fe4184044d0a312ce7d9376))
* **webdav:** per-segment streaming timeout with fast failover ([82d5b99](https://github.com/nzbdav/nzbdav/commit/82d5b997848e6ca704d83e37b8140a7ebd3466e0))


### Bug Fixes

* **api:** correct whitespace formatting in NzbBackupRetentionService ([d418429](https://github.com/nzbdav/nzbdav/commit/d418429fd1216e76bf3c2bf6766da46520c4cab5))
* **api:** skip unpack decoy videos in profiles play selection ([125c46c](https://github.com/nzbdav/nzbdav/commit/125c46ca11e82b0cb13e26119ee1f8278b12c126))
* **api:** skip unpack decoy videos in profiles play selection ([ff7cf12](https://github.com/nzbdav/nzbdav/commit/ff7cf12149830b5450a73c5d5fd2f709876709bc))
* **auth:** invalidate webdav sessions when credentials change ([051c47e](https://github.com/nzbdav/nzbdav/commit/051c47e8c4fcffef9656adccf29e36240a98c085))
* **auth:** invalidate webdav sessions when credentials change ([3437fd6](https://github.com/nzbdav/nzbdav/commit/3437fd6d066dce649987bfff32d6d502c30e7e10))
* **db:** mark SegmentFallbackIds NotMapped for EF ([1920293](https://github.com/nzbdav/nzbdav/commit/19202932b1127102742f2063c0dd79fb1e152381))
* **db:** NZB blob name cleanup and backup retention ([#83](https://github.com/nzbdav/nzbdav/issues/83)) ([0c1c26e](https://github.com/nzbdav/nzbdav/commit/0c1c26ef76ce39bcbdd2c923992304161d06727a))
* **db:** remove orphaned nzb name rows when blobs are cleaned up ([b2b281c](https://github.com/nzbdav/nzbdav/commit/b2b281ca502350ed1a98ed588e7b36b838513f00))
* **health:** align StreamingFailureTracker method names with callers ([77e3e67](https://github.com/nzbdav/nzbdav/commit/77e3e67618814cd16765db651401b7804b11d143))
* **health:** complete streaming failure tracker wiring ([15e8f61](https://github.com/nzbdav/nzbdav/commit/15e8f61935286396cb788c81879956a48ddc5468))
* **health:** hide deleted providers from overview stats ([a56c5ad](https://github.com/nzbdav/nzbdav/commit/a56c5ad9fc6895d8a082fa5c47741d29d65e2c65))
* **health:** hide deleted providers from overview stats ([166a832](https://github.com/nzbdav/nzbdav/commit/166a8327db20c136780f7b759274eadc15ee1073))
* **health:** remove duplicate dav-cleanup audit log block ([e8197ed](https://github.com/nzbdav/nzbdav/commit/e8197edc88c266b85995b742d5cf49de76f39311))
* **nntp:** add connect/auth timeout and dispose failed handshakes ([2b0020b](https://github.com/nzbdav/nzbdav/commit/2b0020bb70f9ff4cf9ad8b88b4d6d6da8227a610))
* **nntp:** add connect/auth timeout and dispose failed handshakes ([7f613b7](https://github.com/nzbdav/nzbdav/commit/7f613b706f69f40046a4d9241793df48b09a02a1))
* **nntp:** count exhausted streaming timeouts toward the breaker ([c139846](https://github.com/nzbdav/nzbdav/commit/c13984690a243bbea8c0587f42fb58cb6123a132))
* **nntp:** count exhausted streaming timeouts toward the breaker ([667dd5a](https://github.com/nzbdav/nzbdav/commit/667dd5a0e2f40fc7ba0f8a199e851deb63899d1a))
* **nntp:** gate individual stat/head requests through prioritized semaphore ([394a1d5](https://github.com/nzbdav/nzbdav/commit/394a1d59b563153eec80cfe5c72a274d8319897a))
* **nntp:** gate individual STAT/HEAD through prioritized semaphore ([fd5e04a](https://github.com/nzbdav/nzbdav/commit/fd5e04aa86cfbeb033b3e2517c20ee6be031ddba))
* **queue:** accept split-rar sets with colliding header volume numbers ([705d7c2](https://github.com/nzbdav/nzbdav/commit/705d7c29e43002feeaae07cf62520c71ddecaf22))
* **queue:** accept split-RAR sets with colliding header volume numbers ([35666aa](https://github.com/nzbdav/nzbdav/commit/35666aa5570d671fd4084ad0335a218ae0d191dc))
* **queue:** create strm files for all video items of a job ([7ac0492](https://github.com/nzbdav/nzbdav/commit/7ac04925f44ff026c263abd169c3d05e4fd91428))
* **queue:** create STRM files for all video items of a job ([2a0c60f](https://github.com/nzbdav/nzbdav/commit/2a0c60f5374d52406b3606d63a4a2588aaabfb52))
* **queue:** decode utf-8 par2 filenames correctly for cjk releases ([53c502f](https://github.com/nzbdav/nzbdav/commit/53c502fccb185c87abe98163cb40e84a9588c138))
* **queue:** decode UTF-8 PAR2 filenames for CJK releases ([9ad9fbe](https://github.com/nzbdav/nzbdav/commit/9ad9fbe141f252ebbbb53de14dfb6bfde70d71fb))
* **queue:** dedupe and order NZB segments by number at parse time ([ff90c9d](https://github.com/nzbdav/nzbdav/commit/ff90c9d8a99460bfb8f42a1644a9089b535f2261))
* **queue:** dedupe and order nzb segments by segment number at parse time ([b9b960b](https://github.com/nzbdav/nzbdav/commit/b9b960b60b0169370524a0668979936061af58b1))
* **sab:** avoid per-slot provider snapshots for queued items ([ca761ee](https://github.com/nzbdav/nzbdav/commit/ca761eee92452bab0e2b56c6ce7fd62c64eda4eb))
* **sab:** avoid per-slot provider snapshots for queued items in mode=queue ([78b9276](https://github.com/nzbdav/nzbdav/commit/78b9276cc64068032b2bff5b9039497dfd9b3dd0))
* **sab:** avoid per-slot provider snapshots for queued items in mode=queue ([1118465](https://github.com/nzbdav/nzbdav/commit/111846537bc33d5d0888d2a00cae134e65ad8270))
* **ui:** stack queue provider usage one per line ([6b5d297](https://github.com/nzbdav/nzbdav/commit/6b5d297ac5c0a4546a6dd93c183fbd28dd8c52eb))
* **ui:** stack queue provider usage one per line ([bc1d3ee](https://github.com/nzbdav/nzbdav/commit/bc1d3eefc4e2f0a11d2166de1ccd11b876515cfd))
* **webdav:** cap segment prefetch at http range end ([e67cb37](https://github.com/nzbdav/nzbdav/commit/e67cb37d2da3fc3d9c5ba507af4d486fd1a8bafe))
* **webdav:** fall back to slow seek when fast-seek body read fails ([e8a0202](https://github.com/nzbdav/nzbdav/commit/e8a0202d68bee375f50fe757633bc771d311a446))
* **webdav:** fall back to slow seek when fast-seek body read fails ([3a2a910](https://github.com/nzbdav/nzbdav/commit/3a2a910eb16d56f01c339acb34c843e2853ef5fa))
* **webdav:** sanitize dav path components for windows-invalid names ([27161ba](https://github.com/nzbdav/nzbdav/commit/27161baa981846f3e2fb630101b2c0aaf5a933b3))
* **webdav:** sanitize Dav path components for Windows-invalid names ([1d2beaa](https://github.com/nzbdav/nzbdav/commit/1d2beaae1d51e67b25136c4a99df16ad76ed8a09))

## [0.7.18](https://github.com/nzbdav/nzbdav/compare/v0.7.17...v0.7.18) (2026-07-14)


### Features

* **api:** add database backup endpoints ([ba8b32c](https://github.com/nzbdav/nzbdav/commit/ba8b32c71b43ef88f74c51653c95e1ea17203eb2))
* **db:** add backup store with manifests and retention pruning ([bfc0a18](https://github.com/nzbdav/nzbdav/commit/bfc0a18a94140c2b16417017cf373cf0adaf25e9))
* **db:** add database backup task and daily scheduler ([7daa01d](https://github.com/nzbdav/nzbdav/commit/7daa01d70a4f30307f0d2faaf440748acff4c349))
* **db:** add sqlite .sql dump and import utilities ([61442b1](https://github.com/nzbdav/nzbdav/commit/61442b1f3a031083c37d4ff820be29ecdf83bb94))
* **db:** integrated database backup and restore ([ef4da2e](https://github.com/nzbdav/nzbdav/commit/ef4da2e8dbf0b5096cb5cca9455be6d4089051bb))
* **db:** stage guided restore and swap databases during maintenance ([9777b08](https://github.com/nzbdav/nzbdav/commit/9777b086b24e8dc004ea6895b738724b02983f08))
* **docker:** restart loop for staged database restores ([71fc260](https://github.com/nzbdav/nzbdav/commit/71fc26057470bc1f001b587a6923477d5ab6ca1b))
* make ThreadPool limits configurable ([00f1a4a](https://github.com/nzbdav/nzbdav/commit/00f1a4ac7a9eb5a0de091a5788dc1a97b4fc5d3f))
* make ThreadPool limits configurable via env vars ([b8e8a98](https://github.com/nzbdav/nzbdav/commit/b8e8a98ccae09d59ec69fc7bcfbbf277cd9ddad9))
* **ui:** add backup and restore settings tab ([06d218e](https://github.com/nzbdav/nzbdav/commit/06d218ef33628e8a697d104c14744a05200d0ab1))
* **ui:** notify non-release builds of new commits on main ([85b5f22](https://github.com/nzbdav/nzbdav/commit/85b5f22e7a3b7cf5731cf4b2c750f5856ae48d05))
* **ui:** notify stale source and dev builds ([00daa25](https://github.com/nzbdav/nzbdav/commit/00daa25353b1331dd48e80bceb1ca4aa3a6565f2))
* **websocket:** add bounded outbound backpressure ([5c8b6b5](https://github.com/nzbdav/nzbdav/commit/5c8b6b56bac4424d6b2c13fdb566df2676232a08))
* **websocket:** add bounded outbound backpressure ([4758436](https://github.com/nzbdav/nzbdav/commit/4758436cf9850d7e40dbda447a86a9c913018be0))


### Bug Fixes

* **deps:** bump the github-actions group with 4 updates ([b60361f](https://github.com/nzbdav/nzbdav/commit/b60361f0e49ccebbe3091cf9551eccd08a8ba841))
* **ui:** check main source clones for new commits ([992489d](https://github.com/nzbdav/nzbdav/commit/992489df12a97c27d253f8524bae900e853a82c6))
* **webdav:** clamp infinite-depth PROPFIND to depth 1 ([9b9638f](https://github.com/nzbdav/nzbdav/commit/9b9638f7f52e6632b435912a8127a01267fe2d7c))


### Performance Improvements

* **webdav:** stream and order directory listings from SQL ([425b9cc](https://github.com/nzbdav/nzbdav/commit/425b9ccfe4764ba6512d4468c7e3b54b73597cd3)), closes [#238](https://github.com/nzbdav/nzbdav/issues/238)
* **webdav:** stream and order large directory listings ([478a243](https://github.com/nzbdav/nzbdav/commit/478a2435d76afdd94adfdaf98fa3711da7261991))

## [0.7.17](https://github.com/nzbdav/nzbdav/compare/v0.7.16...v0.7.17) (2026-07-14)


### Bug Fixes

* **auth:** harden frontend session key and cookie settings ([d1833b4](https://github.com/nzbdav/nzbdav/commit/d1833b4c5cd410e7efe4f24834a30a1eb0f702f8)), closes [#219](https://github.com/nzbdav/nzbdav/issues/219)
* **db:** delete DavItems batches by stored Id text to survive casing mismatch ([e89e920](https://github.com/nzbdav/nzbdav/commit/e89e920775d5d04f12d0b516a5ce458ce2dc7e9e))
* **db:** don't read ConfigItems before migrations on fresh databases ([e89426a](https://github.com/nzbdav/nzbdav/commit/e89426ac670571181988e66ca092a4e4e6e116f6))
* **db:** drain seeded empty dirs before asserting zero removals ([ffb36af](https://github.com/nzbdav/nzbdav/commit/ffb36af5044ebd6f6a1a04608e17886c38c946b3))
* **deps:** bump react-router packages to 8.2.0 ([bff957d](https://github.com/nzbdav/nzbdav/commit/bff957da77939f285e2a039b493d772f144e7cf1))
* fresh-database startup crash and Remove Orphaned Files stuck in Running ([5cad058](https://github.com/nzbdav/nzbdav/commit/5cad058284ef15faecbc73d13cdfee156d27cd9a))
* **nntp:** log known transport failures without stack dumps ([ba80566](https://github.com/nzbdav/nzbdav/commit/ba805662d2308a5f5c8779cda1f373c71f3df3b5))
* **nntp:** log known transport failures without stack dumps ([47930e2](https://github.com/nzbdav/nzbdav/commit/47930e2d705980afbad2f350deff996498b8b816))
* **nntp:** route pipelined queue fetches through per-segment failover ([884b0d0](https://github.com/nzbdav/nzbdav/commit/884b0d06b6f98d436de648405c3ffa52e65c7c59))
* **nntp:** route pipelined queue fetches through per-segment failover ([0b3a566](https://github.com/nzbdav/nzbdav/commit/0b3a5660fb1b04f9d79a8c7c5a3812f0dfb2c808))
* **nntp:** stop invalid segment-id loops with 404 + repair ([0c89f9c](https://github.com/nzbdav/nzbdav/commit/0c89f9cf55689cd869100c998b10dd3c02075c47))
* **nntp:** stop invalid segment-id loops with 404 + repair ([4d6d5f2](https://github.com/nzbdav/nzbdav/commit/4d6d5f240c112c405104ccbe9ea7b1b76990dde2))
* **queue:** resolve metrics keys to hosts in live provider websocket ([4f27658](https://github.com/nzbdav/nzbdav/commit/4f27658e6bd9c0f8593395711e72d254b0880993))
* RemoveUnlinkedFilesTask CI failures (Guid casing + dash pipefail) ([db3abd8](https://github.com/nzbdav/nzbdav/commit/db3abd810ad1e3f5dabce8678aeb16ecce0c42a6))
* SSR build ignoring custom server entry under Vite 8 ([81877c4](https://github.com/nzbdav/nzbdav/commit/81877c4319de7bb7c442d6356cdd5e1a0f82210b))
* **ui:** add .js extension to proxy-path import for Node ESM ([83d5612](https://github.com/nzbdav/nzbdav/commit/83d56122a4414be7a0d7d56f00e42f7cbdaafca9))
* **ui:** add frontend websocket hub heartbeat ([96766da](https://github.com/nzbdav/nzbdav/commit/96766dad75986ae99c1ca57397f0e659248ce9cd)), closes [#225](https://github.com/nzbdav/nzbdav/issues/225)
* **ui:** add security response headers for admin UI ([a42292a](https://github.com/nzbdav/nzbdav/commit/a42292a7cbfa728ee59dde52ae8cf76adb602c1f)), closes [#215](https://github.com/nzbdav/nzbdav/issues/215)
* **ui:** allow editing provider Already Used offset ([cce1b92](https://github.com/nzbdav/nzbdav/commit/cce1b920252ba0ac3807c63b77ef867e0a4f780d)), closes [#256](https://github.com/nzbdav/nzbdav/issues/256)
* **ui:** bound frontend websocket subscriptions and payload ([247c60d](https://github.com/nzbdav/nzbdav/commit/247c60de6fa6e5f235c1d9ee6ed37e4a33fa06b2)), closes [#220](https://github.com/nzbdav/nzbdav/issues/220)
* **ui:** derive Remove Orphaned Files running state from user-initiated runs ([9aa543a](https://github.com/nzbdav/nzbdav/commit/9aa543aa586a200c2c9fed6dd2c8147051f456af))
* **ui:** disable Link prefetch on explore directory links ([abc48f7](https://github.com/nzbdav/nzbdav/commit/abc48f7e42838a9e031f0751d675e653cef9b794)), closes [#135](https://github.com/nzbdav/nzbdav/issues/135)
* **ui:** disable X-Powered-By on SSR sub-app ([b8fdc9a](https://github.com/nzbdav/nzbdav/commit/b8fdc9ad5bf69fbcb1d8ad3553d95723a8511d5b)), closes [#221](https://github.com/nzbdav/nzbdav/issues/221)
* **ui:** frontend audit and UX batch ([9ecc16e](https://github.com/nzbdav/nzbdav/commit/9ecc16e05d430c0af7ce2eecc6b78e4ba982acb4))
* **ui:** harden provider id generation and duplicate-host rendering ([2547e67](https://github.com/nzbdav/nzbdav/commit/2547e672f4517eeba8a6c4c94115fb3d034974d5))
* **ui:** omit NZB file accept filter on iOS ([09970d4](https://github.com/nzbdav/nzbdav/commit/09970d4d5671c3145a0fc034836c3c7e6ce48494)), closes [#140](https://github.com/nzbdav/nzbdav/issues/140)
* **ui:** quiet expected BackendUnavailableError noise during startup grace ([86da280](https://github.com/nzbdav/nzbdav/commit/86da2804d9a95250f1857479d835aed61cd7822d))
* **ui:** quiet expected BackendUnavailableError noise during startup grace ([5865305](https://github.com/nzbdav/nzbdav/commit/5865305854f7b8b3fb4aed22e74ea499200fc68d))
* **ui:** resolve frontend startup crash from extensionless proxy-path import ([6eaf10d](https://github.com/nzbdav/nzbdav/commit/6eaf10d84d920c43a7c1ba347d99478e92ac6ed6))
* **ui:** revalidate root loader when crossing login layout boundary ([b85f6c8](https://github.com/nzbdav/nzbdav/commit/b85f6c8263924f9f9bfdcb52c5308890c9cccf4a)), closes [#226](https://github.com/nzbdav/nzbdav/issues/226)
* **ui:** self-host Inter and drop Google Fonts CDN ([0ad667c](https://github.com/nzbdav/nzbdav/commit/0ad667ce4bfab295693b84522dd9d04a770f84a0)), closes [#222](https://github.com/nzbdav/nzbdav/issues/222)
* **ui:** share one multiplexed WebSocket per browser tab ([57bc4a8](https://github.com/nzbdav/nzbdav/commit/57bc4a877819729a2956ef153eae5d35bc31f215)), closes [#224](https://github.com/nzbdav/nzbdav/issues/224)
* **ui:** skip root config revalidation on routine mutations ([9b8c257](https://github.com/nzbdav/nzbdav/commit/9b8c257996204cf80ab462af60ae9fb65769fb6f)), closes [#226](https://github.com/nzbdav/nzbdav/issues/226)
* **ui:** strip iOS accept attribute after mount to avoid hydration mismatch ([5be48a5](https://github.com/nzbdav/nzbdav/commit/5be48a5f9ff48937b8f1d138af6fbbf4d4a3814c)), closes [#140](https://github.com/nzbdav/nzbdav/issues/140)
* **ui:** use discover=none instead of prefetch=none on explore links ([eb54c8a](https://github.com/nzbdav/nzbdav/commit/eb54c8a475405abf51d6ae7c1458b102162d917c)), closes [#135](https://github.com/nzbdav/nzbdav/issues/135)
* **usenet:** key provider usage metrics by ProviderId ([192a047](https://github.com/nzbdav/nzbdav/commit/192a047c7a61133200062a5d336588e08dcf8731))
* **usenet:** key provider usage metrics by ProviderId ([f6f20b3](https://github.com/nzbdav/nzbdav/commit/f6f20b37f4f0c3b00631b4ecaebd0bed89eaee2a))
* **webdav:** dequeue dav cleanup items with lowercase guid ids ([92f5ca6](https://github.com/nzbdav/nzbdav/commit/92f5ca61fe0836b166de34916515cd31655a2c71))
* **webdav:** drop pipefail dependency from Linux library scan ([5ef3490](https://github.com/nzbdav/nzbdav/commit/5ef3490bbb80f9037d9228581c6dec4a8f313d9a))
* **webdav:** guarantee RemoveUnlinkedFiles terminates and reject concurrent runs ([064e70b](https://github.com/nzbdav/nzbdav/commit/064e70b1e016fd27d05928d82b3a77de46b7e269))
* **webdav:** handle lowercase GUIDs in DAV cleanup queue ([a2eea70](https://github.com/nzbdav/nzbdav/commit/a2eea70427be42ab93176b8c232b2e60296d56ba))

## [0.7.16](https://github.com/nzbdav/nzbdav/compare/v0.7.15...v0.7.16) (2026-07-13)


### Features

* **ui:** add 1h Overview activity window ([a5aefe7](https://github.com/nzbdav/nzbdav/commit/a5aefe777bfdff8969623ab2a4bd431e49e1216b))
* **ui:** Overview 1h window and queue/nav polish ([976d090](https://github.com/nzbdav/nzbdav/commit/976d09033a79646e635e42c6d7674d74ccde0ab0))


### Bug Fixes

* **api:** compare profile tokens in constant time ([7ed50fa](https://github.com/nzbdav/nzbdav/commit/7ed50fa44eedd45696f48006135d316b6e27b19d))
* **api:** compare profile tokens in constant time ([22245ba](https://github.com/nzbdav/nzbdav/commit/22245ba00dd4f9a37dfff49908fb5719af226e19))
* **api:** validate forwarded headers and sanitize proxy ([ac84f98](https://github.com/nzbdav/nzbdav/commit/ac84f9899b40f365073c41549e23e368c767cf2b))
* **api:** validate forwarded headers and sanitize proxy ([1efa170](https://github.com/nzbdav/nzbdav/commit/1efa170d584271cd4ff020f2963e40f78dd96ac3))
* **auth:** close username-enumeration timing oracle ([d31e0bf](https://github.com/nzbdav/nzbdav/commit/d31e0bfbc8583c7f4c84b1bcc985d0dbb45fba6c))
* **auth:** close username-enumeration timing oracle ([6562cb1](https://github.com/nzbdav/nzbdav/commit/6562cb16267f94cd570f64963cf4aa787fc4e994))
* **auth:** hmac-key password verification cache ([83f7f20](https://github.com/nzbdav/nzbdav/commit/83f7f20e4fbadbabbff1c6758ebc4a7f6afe7616))
* **auth:** hmac-key password verification cache ([89610fa](https://github.com/nzbdav/nzbdav/commit/89610fa3ba617059713d62a818e8ddc1818b9c15))
* **db:** return not-found for non-guid /.ids lookups ([4f3311e](https://github.com/nzbdav/nzbdav/commit/4f3311ee102e9310ca44b6a960800f706ddaac1a))
* **health:** fix organized-links cache key and parse skips ([a7281fb](https://github.com/nzbdav/nzbdav/commit/a7281fb3bda090cf60ca34f626dd1576db711a2b))
* **health:** floor NextHealthCheck to avoid hot-loops ([ce639d9](https://github.com/nzbdav/nzbdav/commit/ce639d908997296fd2a7994dd393bf80e52608e0))
* **nntp:** drain replaced clients before disposal ([39dcaa3](https://github.com/nzbdav/nzbdav/commit/39dcaa31f01a88f6a534173b88151bcec6816a67))
* **nntp:** drain replaced clients before disposal ([4700def](https://github.com/nzbdav/nzbdav/commit/4700def013a35a8fafac1b564c3cec79a92951ea))
* **nntp:** drain test hook inline without background loop ([772d77d](https://github.com/nzbdav/nzbdav/commit/772d77db03ac47d588dca860e89bbbc5cf244412))
* repair-path link cache, health hot-loops, and pool WS flood ([3aaae33](https://github.com/nzbdav/nzbdav/commit/3aaae3398a74a9e003ce403f566df30831850490))
* **sab:** cap unbounded history responses ([1b2abd9](https://github.com/nzbdav/nzbdav/commit/1b2abd979ca68fe99effe8e3e7d87a118dea7512))
* **sab:** cap unbounded history responses ([a619da0](https://github.com/nzbdav/nzbdav/commit/a619da096b69c0423d88d8c85dd17f4d56fc23ad))
* **sab:** clamp negative history limits to zero ([9e87e4b](https://github.com/nzbdav/nzbdav/commit/9e87e4b18d34bd824f90710be9abfaa49fe27eb8))
* **ui:** include proxy-path in node typecheck project ([7c7f05b](https://github.com/nzbdav/nzbdav/commit/7c7f05bc7c808b9f0d0a472908a884109ec73c22))
* **ui:** keep top-nav version label on one line ([ca268e1](https://github.com/nzbdav/nzbdav/commit/ca268e175c529ed69d8cc6c95635b4acc893743d))
* **ui:** match proxy allowlist on path segment boundaries ([35ea307](https://github.com/nzbdav/nzbdav/commit/35ea307177040632e3b6fa2372cb4d4309ad3859))
* **ui:** match proxy allowlist on path segment boundaries ([026d018](https://github.com/nzbdav/nzbdav/commit/026d0184fbdc2119888794a52863146d9645b0c1))
* **ui:** safe-decode credential rate-limiter path check ([ed0440b](https://github.com/nzbdav/nzbdav/commit/ed0440b99c22ad5ef3157424883ca8c8fefa72f2))
* **ui:** safe-decode paths in proxy auth and compression ([7e1d588](https://github.com/nzbdav/nzbdav/commit/7e1d5886fb487a2a3bb1c5ba6c2e40a56b26272f))
* **ui:** safe-decode paths in proxy auth and compression ([9e51abc](https://github.com/nzbdav/nzbdav/commit/9e51abc8d8bdbb50a11dbaa21e6ae53f9cca9191))
* **ui:** stop counting provider misses as Overview errors ([c79010d](https://github.com/nzbdav/nzbdav/commit/c79010dda4310f7fe6cde60708f0b1eef62ab4b6))
* **ui:** stop counting provider misses as Overview errors ([3484d21](https://github.com/nzbdav/nzbdav/commit/3484d210cd54b10ee19bf0bf8426911a72cea69c))
* **ui:** stop idle providers overflowing the queue Provider column ([48359cd](https://github.com/nzbdav/nzbdav/commit/48359cd52aa542f94ca8405ff9f7a9af3ec76255))
* **usenet:** coalesce connection-pool websocket updates ([4743ff0](https://github.com/nzbdav/nzbdav/commit/4743ff07785b6632aa9b1e55dff73cdb8e874ad1))
* **webdav:** clear partial range outs on parse failure ([8967564](https://github.com/nzbdav/nzbdav/commit/8967564c21de9e55cc4d5214a91c95ab23356084))
* **webdav:** handle malformed and unsatisfiable /view ranges ([ea83312](https://github.com/nzbdav/nzbdav/commit/ea83312722d6210666163f1abf445ff230297775))
* **webdav:** handle malformed and unsatisfiable /view ranges ([0b4da71](https://github.com/nzbdav/nzbdav/commit/0b4da7188aeb81d3ecab0347c499b8b0ee3648a5))

## [0.7.15](https://github.com/nzbdav/nzbdav/compare/v0.7.14...v0.7.15) (2026-07-13)


### Bug Fixes

* **ui:** add .js extension to startup-grace import for node esm ([e60eb6f](https://github.com/nzbdav/nzbdav/commit/e60eb6f62947ec6a2d1b7048133ae2b5167bcd36))

## [0.7.14](https://github.com/nzbdav/nzbdav/compare/v0.7.13...v0.7.14) (2026-07-13)


### Bug Fixes

* **ui:** avoid .server import in root ErrorBoundary ([e9984e6](https://github.com/nzbdav/nzbdav/commit/e9984e6145db5461371c47e1c19426b3a1ac27e1))
* **ui:** unblock release Docker build and gate CI on frontend build ([c1af9a3](https://github.com/nzbdav/nzbdav/commit/c1af9a35ef9081d6086222cd614d62c6e5e7c081))

## [0.7.13](https://github.com/nzbdav/nzbdav/compare/v0.7.12...v0.7.13) (2026-07-13)


### Bug Fixes

* **nntp:** latch circuit breaker trips and log once per trip ([4d42193](https://github.com/nzbdav/nzbdav/commit/4d421936edffb385c1d083fdf016a65d61e18b13))
* **nntp:** pace concurrent connection establishment per provider ([7d130e1](https://github.com/nzbdav/nzbdav/commit/7d130e18c283908d8bf32c26bc67767301d07b00))
* **ui:** drop hardcoded v prefix from displayed app version ([ab9f00d](https://github.com/nzbdav/nzbdav/commit/ab9f00d1e04b28c527a1ae4bdbfea803582efac1))
* **ui:** quiet expected startup noise on no-migration restarts ([748abcd](https://github.com/nzbdav/nzbdav/commit/748abcd41ecd5686666a7c8ad3ec4b4042779be7))
* **ui:** quiet expected startup noise on no-migration restarts ([ae7a43b](https://github.com/nzbdav/nzbdav/commit/ae7a43bdcb7bd423d42d3364cfd7e5a8b84495bb))
* **ui:** restore shell scrolling and polish header/settings ([2ff591e](https://github.com/nzbdav/nzbdav/commit/2ff591e30270047e0e8b35a6f6e61c8501a242a3))
* **ui:** restore top-nav logout menu item styling ([037de3e](https://github.com/nzbdav/nzbdav/commit/037de3efb2c178f4aa32f28b933b377f124d0448))

## [0.7.12](https://github.com/nzbdav/nzbdav/compare/v0.7.11...v0.7.12) (2026-07-13)


### Bug Fixes

* **db:** tolerate pre-existing IX_DavItems_Path index during migration ([2d3cdbe](https://github.com/nzbdav/nzbdav/commit/2d3cdbe0001c4dd9d203c581b2eefaa5518435ff))
* **db:** tolerate pre-existing IX_DavItems_Path index during migration ([9536f27](https://github.com/nzbdav/nzbdav/commit/9536f273255639f7ed490d6fbe31be75c8216eae))

## [0.7.11](https://github.com/nzbdav/nzbdav/compare/v0.7.10...v0.7.11) (2026-07-13)


### Features

* **db:** periodic PRAGMA optimize and WAL checkpoint maintenance ([792bb38](https://github.com/nzbdav/nzbdav/commit/792bb3868b2cad019502fbd15b0af9205bcf5707))
* **ui:** adopt daisyUI theme tokens and shared UI primitives ([3b8b03d](https://github.com/nzbdav/nzbdav/commit/3b8b03d467283f05947d9856099a355dea83a5a2))
* **ui:** daisyUI 5 frontend refresh ([ba040db](https://github.com/nzbdav/nzbdav/commit/ba040db260acb74de66ec36839d812ce2935217f))
* **ui:** migrate logs, watchdog, and watchtower activity pages to daisyUI ([1d97a2f](https://github.com/nzbdav/nzbdav/commit/1d97a2f1fccdd4e8b8b3b2808312cf5485cf5ce8))
* **ui:** migrate overview, queue, explore, health, and search to daisyUI ([78e059a](https://github.com/nzbdav/nzbdav/commit/78e059ad6483cf91486a7208f079668810f45b5e))
* **ui:** rebuild app shell with full-width navbar and settings submenu ([a1e9bc6](https://github.com/nzbdav/nzbdav/commit/a1e9bc66b6bcb0e7d54ebcd647c76b0025b1b77f))
* **ui:** redesign settings and auth flows with shared daisyUI layout ([77e3ff5](https://github.com/nzbdav/nzbdav/commit/77e3ff523f50ce0b6acff1e0bc67e945b3eceb21))


### Bug Fixes

* **auth:** accept configured api key on admin /api endpoints ([b576c91](https://github.com/nzbdav/nzbdav/commit/b576c91ebafc11f198221dfc401bc72f14fee8ea)), closes [#242](https://github.com/nzbdav/nzbdav/issues/242)
* **config:** validate config updates and drop stale usenet.host cache-clear key ([f8b6116](https://github.com/nzbdav/nzbdav/commit/f8b61164fc2a668831fcfeed3b71499dad53349a)), closes [#245](https://github.com/nzbdav/nzbdav/issues/245) [#240](https://github.com/nzbdav/nzbdav/issues/240)
* **db:** bring main database pragmas up to metrics parity ([231c704](https://github.com/nzbdav/nzbdav/commit/231c7049fcb9394e20f745c59b0960463d8cb8b4))
* **db:** make DavItems path rebuild incremental and index-driven ([53cecab](https://github.com/nzbdav/nzbdav/commit/53cecab87d6d035663746c4bdbacd5176df6ebb1))
* **db:** set busy_timeout on metrics connections ([cda51c1](https://github.com/nzbdav/nzbdav/commit/cda51c1303fd294ac0c6037da90948d31f9feb64))
* **db:** silence websocket warnings during database migration ([#270](https://github.com/nzbdav/nzbdav/issues/270)) ([e9e1329](https://github.com/nzbdav/nzbdav/commit/e9e13295f5da8671f6d962703802549c699e51da))
* **db:** SQLite performance and slow migration path rebuild ([d66b0f5](https://github.com/nzbdav/nzbdav/commit/d66b0f52bfc20cd59117ddec752f862056dca0bc))
* **health:** treat NNTP 451 as missing during health checks ([dc407bc](https://github.com/nzbdav/nzbdav/commit/dc407bc59f48687792d78059439427d68eb13329))
* **health:** treat NNTP 451 as missing during health checks ([1decfa9](https://github.com/nzbdav/nzbdav/commit/1decfa94a1073d7697da5e4ad4118f324772796f))
* **ui:** encode category with URLSearchParams in backend client ([546dacd](https://github.com/nzbdav/nzbdav/commit/546dacda3e020100dc7f86f75377deb52d6b0028)), closes [#223](https://github.com/nzbdav/nzbdav/issues/223)
* **ui:** move connection stats to header ([b9f8ff0](https://github.com/nzbdav/nzbdav/commit/b9f8ff0718fdd91b86bf226456dbfd173388e8fe))
* **ui:** restore button pointer cursor and unify provider actions ([34504f3](https://github.com/nzbdav/nzbdav/commit/34504f3317e5b1d4c7385e0fe6a1a31203c12c53))


### Performance Improvements

* **usenet:** cache yEnc headers on the streaming path ([7da84de](https://github.com/nzbdav/nzbdav/commit/7da84de81052a904a70103e03f20b8d3b8ec798d)), closes [#243](https://github.com/nzbdav/nzbdav/issues/243)
* **webdav:** resolve persisted paths with one indexed query ([5fc77c0](https://github.com/nzbdav/nzbdav/commit/5fc77c033b35b70014672ca64d697c7bc299cc8f)), closes [#237](https://github.com/nzbdav/nzbdav/issues/237)

## [0.7.10](https://github.com/nzbdav/nzbdav/compare/v0.7.9...v0.7.10) (2026-07-13)


### Features

* **api:** prefer subtitle-bearing releases during playback failover ([#263](https://github.com/nzbdav/nzbdav/issues/263)) ([2e57b7e](https://github.com/nzbdav/nzbdav/commit/2e57b7eeaf9bdb92d5f64c5c22adfaa3ced70318))
* **api:** sync exclude-filter patterns from remote URLs ([#267](https://github.com/nzbdav/nzbdav/issues/267)) ([87c78cd](https://github.com/nzbdav/nzbdav/commit/87c78cdb34ca0e77672e21a05e0a3d2d83230123))
* **db:** live progress UI for long database migrations ([#269](https://github.com/nzbdav/nzbdav/issues/269)) ([e3bf777](https://github.com/nzbdav/nzbdav/commit/e3bf77725e5a4ebc9b26dac8bbd07236a5eaddcb))
* **nntp:** auto and per-stream max download connections ([#265](https://github.com/nzbdav/nzbdav/issues/265)) ([80e3f6e](https://github.com/nzbdav/nzbdav/commit/80e3f6ea2911607f63ff2cd79c22b01442fb29e9))


### Bug Fixes

* **ui:** use amber warning styles when a new version is available ([75d53cb](https://github.com/nzbdav/nzbdav/commit/75d53cbd6355dc6a9b2621df00c6c90ef24ee017))

## [0.7.9](https://github.com/nzbdav/nzbdav/compare/v0.7.8...v0.7.9) (2026-07-13)


### Bug Fixes

* **usenet:** include file path in missing-article zero-fill warnings ([#251](https://github.com/nzbdav/nzbdav/issues/251)) ([9591d36](https://github.com/nzbdav/nzbdav/commit/9591d36f9c44095489e0f68cdfbcccc6a3402453))

## [0.7.8](https://github.com/nzbdav/nzbdav/compare/v0.7.7...v0.7.8) (2026-07-12)


### Features

* **ui:** restyle sidebar version display as bordered status card ([e043b84](https://github.com/nzbdav/nzbdav/commit/e043b8433391e428df23dcced76717a569e97c62))
* **ui:** sort providers in Usenet settings by type and priority ([#249](https://github.com/nzbdav/nzbdav/issues/249)) ([cf2ccb1](https://github.com/nzbdav/nzbdav/commit/cf2ccb1624ac96b5ace7f3c9fa3d16168dc395e0)), closes [#246](https://github.com/nzbdav/nzbdav/issues/246)
* **usenet:** skip same StorageGroup providers after article 430 ([#250](https://github.com/nzbdav/nzbdav/issues/250)) ([cc1113f](https://github.com/nzbdav/nzbdav/commit/cc1113f77db50f765656226c2b12129fc83674a8)), closes [#244](https://github.com/nzbdav/nzbdav/issues/244)

## [0.7.7](https://github.com/nzbdav/nzbdav/compare/v0.7.6...v0.7.7) (2026-07-12)


### Bug Fixes

* **queue:** heal lazy RAR volume size underestimates ([#211](https://github.com/nzbdav/nzbdav/issues/211)) ([eaa4cf6](https://github.com/nzbdav/nzbdav/commit/eaa4cf66c5260bcc22545ddf5a701bff389471d1)), closes [#168](https://github.com/nzbdav/nzbdav/issues/168)

## [0.7.6](https://github.com/nzbdav/nzbdav/compare/v0.7.5...v0.7.6) (2026-07-12)


### Features

* **ui:** show sidebar notice when a newer release is available ([b9e3702](https://github.com/nzbdav/nzbdav/commit/b9e370251e3a5b9237288685c62c04ea57bda30e))


### Bug Fixes

* **db:** parameterize RemoveUnlinkedFilesTask raw SQL ([#198](https://github.com/nzbdav/nzbdav/issues/198)) ([b2871f9](https://github.com/nzbdav/nzbdav/commit/b2871f9d5762e5e394b82209820323ce371c7fcb)), closes [#186](https://github.com/nzbdav/nzbdav/issues/186)
* **ui:** remove dead /p/ proxy prefix from allowlists ([#201](https://github.com/nzbdav/nzbdav/issues/201)) ([bb7b4f9](https://github.com/nzbdav/nzbdav/commit/bb7b4f95b9274465096813e34d562e9de327a2ff)), closes [#182](https://github.com/nzbdav/nzbdav/issues/182)
* **webdav:** dedupe mid-read failure logs and include User-Agent ([af25f7c](https://github.com/nzbdav/nzbdav/commit/af25f7c537afe61d53cc7e7db12b0e5882f2c8e2))

## [0.7.5](https://github.com/nzbdav/nzbdav/compare/v0.7.4...v0.7.5) (2026-07-12)


### Bug Fixes

* harden RemoveOrphanedFilesScheduler against disposal race and DST clock skew ([#175](https://github.com/nzbdav/nzbdav/issues/175)) ([1f87b28](https://github.com/nzbdav/nzbdav/commit/1f87b28827ccbdd9a160f2e5b12cf43efc89368d))
* **health:** isolate arr failures in repair and skip empty root paths ([#176](https://github.com/nzbdav/nzbdav/issues/176)) ([e52e9cf](https://github.com/nzbdav/nzbdav/commit/e52e9cf6bbc3e6d4c38774fe94c9108d24e18238))
* **ui:** keep login page outside the app shell ([36efa4a](https://github.com/nzbdav/nzbdav/commit/36efa4abfdec8dab5470ac009c47ac0349e527e3))

## [0.7.4](https://github.com/nzbdav/nzbdav/compare/v0.7.3...v0.7.4) (2026-07-12)


### Features

* **ui:** remind users to speed-test before enabling NNTP pipelining ([8eb61e0](https://github.com/nzbdav/nzbdav/commit/8eb61e0d2d38c203be7bf1566a9449f031b6fc94))


### Bug Fixes

* **sab:** adopt elfhosted addfile nzbname + JSON converter fallback ([#164](https://github.com/nzbdav/nzbdav/issues/164)) ([3fe3d7a](https://github.com/nzbdav/nzbdav/commit/3fe3d7ab7874f7c49f34a783bf073d187a8f4cfa))
* **ui:** adopt elfhosted healthz, ErrorBoundary, unhandledRejection hardening ([#166](https://github.com/nzbdav/nzbdav/issues/166)) ([578c77b](https://github.com/nzbdav/nzbdav/commit/578c77b99fc9e4ce8df30a909d00a1b6111f2f31))
* **ui:** keep Overview route free of .server imports ([aec1426](https://github.com/nzbdav/nzbdav/commit/aec142635e1153b1f959fee58e3a1347697b57fb))
* **usenet:** resolve masked passwords for speed test and connection test ([#173](https://github.com/nzbdav/nzbdav/issues/173)) ([dc8d89a](https://github.com/nzbdav/nzbdav/commit/dc8d89a45d3d042e7ec5ed8f01249f1d603e08a2))
* **webdav:** adopt elfhosted PROPFIND resourcetype XElement clone ([#165](https://github.com/nzbdav/nzbdav/issues/165)) ([13220c1](https://github.com/nzbdav/nzbdav/commit/13220c1e399481caf0bee9f4f2aa5ec66cfd1038))
* **webdav:** harden RemoveUnlinkedFiles against partial library scans ([#172](https://github.com/nzbdav/nzbdav/issues/172)) ([bb38192](https://github.com/nzbdav/nzbdav/commit/bb38192201a8879063629ad46b4c079e4ac2b03f))
* **webdav:** log RAR header parse failures without stack dumps ([bedd22a](https://github.com/nzbdav/nzbdav/commit/bedd22a39bf6fd4599e419b0b8ae0518137b0055))


### Performance Improvements

* **ui:** speed up Overview with sectioned stats and 24h rollups ([#174](https://github.com/nzbdav/nzbdav/issues/174)) ([d3bc59a](https://github.com/nzbdav/nzbdav/commit/d3bc59acff8d0f7ecb0abbd60162cea400b3de4a))

## [0.7.3](https://github.com/nzbdav/nzbdav/compare/v0.7.2...v0.7.3) (2026-07-12)


### Features

* adopt Pukabyte fork repair, queue, and logging fixes ([#156](https://github.com/nzbdav/nzbdav/issues/156)) ([4b39e20](https://github.com/nzbdav/nzbdav/commit/4b39e206575dd6741d1dc30e4826f5e531414ea2))
* **db:** adopt elfhosted SAB history retention ([#159](https://github.com/nzbdav/nzbdav/issues/159)) ([0a6b46e](https://github.com/nzbdav/nzbdav/commit/0a6b46efc8399e2942b80017250ffac087cc002e))
* **health:** adopt elfhosted health-check retention and reset ([#78](https://github.com/nzbdav/nzbdav/issues/78)) ([#157](https://github.com/nzbdav/nzbdav/issues/157)) ([e7aadf0](https://github.com/nzbdav/nzbdav/commit/e7aadf0853cf2969cee4cabe5afa847d58ea3a0d))
* **sab:** expose history completed timestamp in API and UI ([#153](https://github.com/nzbdav/nzbdav/issues/153)) ([37ce880](https://github.com/nzbdav/nzbdav/commit/37ce88095fa593e5190dcf0f5c2d4e553ce60983)), closes [#66](https://github.com/nzbdav/nzbdav/issues/66)
* **ui:** hyperlink history job names to explore folders ([#152](https://github.com/nzbdav/nzbdav/issues/152)) ([0f2f9d7](https://github.com/nzbdav/nzbdav/commit/0f2f9d7bc72d3ea335a6a708ed29a1c8ff524198)), closes [#72](https://github.com/nzbdav/nzbdav/issues/72)


### Bug Fixes

* **db:** adopt elfhosted read-only SQLite PRAGMA hardening ([#161](https://github.com/nzbdav/nzbdav/issues/161)) ([a0f3502](https://github.com/nzbdav/nzbdav/commit/a0f3502f267c3097563606a9af43d9b9d77aa86b))
* **db:** repair empty categories and harden explore paths ([#155](https://github.com/nzbdav/nzbdav/issues/155)) ([04b841b](https://github.com/nzbdav/nzbdav/commit/04b841b8dd6b9f73083f3f3a64407ac5f10d8ff1)), closes [#48](https://github.com/nzbdav/nzbdav/issues/48) [#94](https://github.com/nzbdav/nzbdav/issues/94)
* **ui:** return 405 for POST requests to index route ([#148](https://github.com/nzbdav/nzbdav/issues/148)) ([0c3f304](https://github.com/nzbdav/nzbdav/commit/0c3f304de2f9fd8ecdb2a199553e63b8d82fc833)), closes [#100](https://github.com/nzbdav/nzbdav/issues/100)
* **usenet:** log host/port on test-connection failures ([#150](https://github.com/nzbdav/nzbdav/issues/150)) ([6a5de27](https://github.com/nzbdav/nzbdav/commit/6a5de27ab5153d21e0bed32597c55623c1b395b3)), closes [#57](https://github.com/nzbdav/nzbdav/issues/57)
* **webdav:** guard empty extension in PathUtil.ReplaceExtension ([#151](https://github.com/nzbdav/nzbdav/issues/151)) ([e699567](https://github.com/nzbdav/nzbdav/commit/e6995670b1030379e4c501868a0d884f188136ce)), closes [#50](https://github.com/nzbdav/nzbdav/issues/50)

## [0.7.2](https://github.com/nzbdav/nzbdav/compare/v0.7.1...v0.7.2) (2026-07-11)


### Bug Fixes

* **nntp:** route pipelined fetches through UsenetSharp batch API ([e019278](https://github.com/nzbdav/nzbdav/commit/e019278061daacc7736f5e9dfa7192e70dbcf882))

## [0.7.1](https://github.com/nzbdav/nzbdav/compare/v0.7.0...v0.7.1) (2026-07-11)


### Bug Fixes

* **metrics:** restore live telemetry after upgrades ([48e4bea](https://github.com/nzbdav/nzbdav/commit/48e4bea57520403fde2cd9c3646f22190abd9287))
* **ui:** avoid duplicate version prefix ([8db7c72](https://github.com/nzbdav/nzbdav/commit/8db7c72e697d3c14b3157a0c0beb6d6cf1bc650a))

## [0.7.0](https://github.com/nzbdav/nzbdav/compare/v0.6.14...v0.7.0) (2026-07-11)


### ⚠ BREAKING CHANGES

* Introduces new persistence schemas and operational configuration for the 0.7 release line.

### Features

* add proactive Usenet discovery and playback resilience ([97f9baa](https://github.com/nzbdav/nzbdav/commit/97f9baa4eed66802cecfabce4bd68e8caa3f3960))
* **ui:** expose observability and discovery workflows ([e4300e9](https://github.com/nzbdav/nzbdav/commit/e4300e9c3738f561b4a3e0bd6257ae81faa61a42))

## [0.6.14](https://github.com/nzbdav/nzbdav/compare/v0.6.13...v0.6.14) (2026-07-11)


### Features

* **ui:** add pipelined article request toggle ([ae929f6](https://github.com/nzbdav/nzbdav/commit/ae929f6d45a99882b4caf1ae739e2f178f80f3b2))
* **webdav:** allow disabling pipelined article requests ([823735e](https://github.com/nzbdav/nzbdav/commit/823735e746a5b11cf5cb4616fe66aaeb9c81cd37))


### Bug Fixes

* **deps:** bump UsenetSharp to 2.0.2 ([6723e43](https://github.com/nzbdav/nzbdav/commit/6723e432c9859608b1a2ac3d551cf246059a43ec))
* **nntp:** retry clean article misses on primary provider ([2168830](https://github.com/nzbdav/nzbdav/commit/21688302a325ff9e1e6677a6b7137790a36bcb84))

## [0.6.13](https://github.com/nzbdav/nzbdav/compare/v0.6.12...v0.6.13) (2026-07-11)


### Bug Fixes

* **deps:** bump UsenetSharp 1.2.4 ([c6e9e92](https://github.com/nzbdav/nzbdav/commit/c6e9e92633c469f29489267961c1f3eaecc7fad6))
* **deps:** bump UsenetSharp to 2.0.0 ([a0a285e](https://github.com/nzbdav/nzbdav/commit/a0a285ec624766a319e776d00f22eba0adc5e032))
* **deps:** bump UsenetSharp to 2.0.1 ([b65cfd1](https://github.com/nzbdav/nzbdav/commit/b65cfd112e5abaff4fb9601269c3804df918932d))
* **nntp:** stop reusing connections poisoned by cancellation ([c9ccaf6](https://github.com/nzbdav/nzbdav/commit/c9ccaf6f38f2c1d7ac8f97bb7c8074d3a072b9d1))

## [0.6.12](https://github.com/nzbdav/nzbdav/compare/v0.6.11...v0.6.12) (2026-07-10)


### Bug Fixes

* **nntp:** treat unexpected responses as retryable connection failures ([68b4a01](https://github.com/nzbdav/nzbdav/commit/68b4a01d135e2209802f6f79a9964e102621317d))
* **webdav:** log human-readable errors for known download failures ([4e9967f](https://github.com/nzbdav/nzbdav/commit/4e9967f33dcee07d4ea5ce6af19c99dd59569fc9))

## [0.6.11](https://github.com/nzbdav/nzbdav/compare/v0.6.10...v0.6.11) (2026-07-10)


### Bug Fixes

* **ui:** build custom server with Vite environments ([39dc559](https://github.com/nzbdav/nzbdav/commit/39dc55912b3f9a056805e967fa4c29f2b2899c56))

## [0.6.10](https://github.com/nzbdav/nzbdav/compare/v0.6.9...v0.6.10) (2026-07-10)


### Bug Fixes

* **deps:** consume UsenetSharp from NuGet.org ([bfe8586](https://github.com/nzbdav/nzbdav/commit/bfe8586aac02444cc732334593f42835b02d6f8a))
* **docker:** image contents ([e67542f](https://github.com/nzbdav/nzbdav/commit/e67542f9f012dfa802562590dcfcca178ef503e7))

## [0.6.9](https://github.com/nzbdav/nzbdav/compare/v0.6.8...v0.6.9) (2026-07-10)


### Features

* modernize console logging ([ad3f3ba](https://github.com/nzbdav/nzbdav/commit/ad3f3baebc2fab5b945429256323c5e8be90ad7e))
* **ui:** add tailwind design foundation ([2a4f4f4](https://github.com/nzbdav/nzbdav/commit/2a4f4f44a5c15e84a50eddb61b9a9cea4e977b80))
* **ui:** restyle application shell ([d91d211](https://github.com/nzbdav/nzbdav/commit/d91d211d4373dd2c98c94b712010f4cf40a32fd1))
* **ui:** restyle authentication screens ([7ad644b](https://github.com/nzbdav/nzbdav/commit/7ad644b43d3339d150a9b22a9de25ab40db6339f))
* **ui:** restyle explore and health views ([bb7a07c](https://github.com/nzbdav/nzbdav/commit/bb7a07c0c33f2c49661bbc437ddee7dc5f5cd3ef))
* **ui:** restyle queue interface ([b924989](https://github.com/nzbdav/nzbdav/commit/b92498957606d8849098a1d66eb0cbf741c6b806))
* **ui:** restyle settings interface ([cdac26e](https://github.com/nzbdav/nzbdav/commit/cdac26e332690573742bd7f5cc3e73dafc327a9f))


### Bug Fixes

* **ci:** publish only v-prefixed release tags ([55329a6](https://github.com/nzbdav/nzbdav/commit/55329a6c76cb3a09c2dc4268fdc48571bfd6fcfc))
* **deps:** Bump @types/node from 25.9.5 to 26.1.0 in /frontend ([#24](https://github.com/nzbdav/nzbdav/issues/24)) ([e9d7620](https://github.com/nzbdav/nzbdav/commit/e9d7620e0fbca372fd56aaab1db469a206b9dd69))
* **deps:** Bump http-proxy-middleware from 3.0.7 to 4.1.1 in /frontend ([#26](https://github.com/nzbdav/nzbdav/issues/26)) ([849bbf4](https://github.com/nzbdav/nzbdav/commit/849bbf4247327812d1540596bcdf6dfdc268c073))
* **deps:** bump SharpCompress from 0.39.0 to 0.49.1 ([f0a1788](https://github.com/nzbdav/nzbdav/commit/f0a1788a946ff1599c160de522c2cb58fed893e3))
* **deps:** Bump typescript from 5.9.3 to 6.0.3 in /frontend ([#20](https://github.com/nzbdav/nzbdav/issues/20)) ([2584aa9](https://github.com/nzbdav/nzbdav/commit/2584aa961f0595bedd8d668715620fe4057ccc9e))
* **ui:** handle unavailable WebDAV directories ([ae29142](https://github.com/nzbdav/nzbdav/commit/ae2914211fda737cae065af4f1e378844f0f6f43))

## [0.6.8](https://github.com/nzbdav/nzbdav/compare/v0.6.7...v0.6.8) (2026-07-10)


### Features

* **usenet:** consume chunked decoded-body API ([d0996d3](https://github.com/nzbdav/nzbdav/commit/d0996d31822c15752144aff2f306f360c60e21a2))
* **usenet:** keep paused streaming connections warm longer ([fe7a779](https://github.com/nzbdav/nzbdav/commit/fe7a7790624bd1e7f8a7384b22062e8199035b5b))
* **usenet:** pipeline segment BODY requests ([b911d06](https://github.com/nzbdav/nzbdav/commit/b911d06e17d4199a1f9b1c8fcfa23cf0dedc7f2a))
* **webdav:** persist segment ranges for arithmetic seeks ([c22650e](https://github.com/nzbdav/nzbdav/commit/c22650ed03c81f1dc1ead42c15ecd1c5cb58c6f9))


### Bug Fixes

* **api:** mask stored credentials in config responses ([8dfedb2](https://github.com/nzbdav/nzbdav/commit/8dfedb28a6a1c727946522267019c0088ad61c5f))
* **api:** stop returning raw exception messages ([0e2accd](https://github.com/nzbdav/nzbdav/commit/0e2accd3da421d30347be6f434d4fcc248e5dcab))
* **arr:** contain monitoring loop failures ([9d7aed2](https://github.com/nzbdav/nzbdav/commit/9d7aed25de87176b87166638440242e476d031bc))
* **arr:** return parent directories in root-first order ([e76531b](https://github.com/nzbdav/nzbdav/commit/e76531bca4ebc639c5c8de67a28e07300a3aa23e))
* **auth:** cache successful WebDAV password checks ([ba60894](https://github.com/nzbdav/nzbdav/commit/ba608945aabc8c77172442476ddfcad7bbe4a6f8))
* **auth:** compare API and download keys in constant time ([a6541e4](https://github.com/nzbdav/nzbdav/commit/a6541e4cbff9e832e8d1010fb848209328a057f6))
* **auth:** preserve sessions across development reloads ([d293df1](https://github.com/nzbdav/nzbdav/commit/d293df1b995a41c10584a5f22abcf3a8bc592f87))
* **backend:** enable server garbage collection ([1169b00](https://github.com/nzbdav/nzbdav/commit/1169b007723c61cdfd2703e431c94be0e9d0a119))
* **backend:** respect configured logging level ([afc72d3](https://github.com/nzbdav/nzbdav/commit/afc72d3d686a6d98a688d4c68f7be4d269098999))
* **ci:** deploy Docker images when release-please creates a release ([01eb27a](https://github.com/nzbdav/nzbdav/commit/01eb27ad61b66c292ce9a0df8c1fcca85122d367))
* **ci:** drop unsupported configfile test flag ([943863c](https://github.com/nzbdav/nzbdav/commit/943863c69275a6dc8d2b656241929cdf5b95adc0))
* **config:** cache parsed structured settings ([1a5e70b](https://github.com/nzbdav/nzbdav/commit/1a5e70bbf881f449e701bfd6d2ca5f8fc1836b8a))
* **db:** cache deserialized streaming metadata ([3eec4c0](https://github.com/nzbdav/nzbdav/commit/3eec4c05e0b79ef3a3ea291b5e0d2e63a368552a))
* **db:** disable tracking for read-only queries ([1d70198](https://github.com/nzbdav/nzbdav/commit/1d70198815ab6516b4f07b5896cf79bf21bb0c34))
* **db:** enable WAL and busy timeout ([56cac83](https://github.com/nzbdav/nzbdav/commit/56cac83a005b666f934645c7835f1711ea958960))
* **deps:** bump react-router packages to 8.1.0 ([a7fb1d2](https://github.com/nzbdav/nzbdav/commit/a7fb1d2e186302f0b4003aec971658793cc2689c))
* **deps:** bump UsenetSharp to 1.2.2 ([3c24411](https://github.com/nzbdav/nzbdav/commit/3c2441120823a71c2e457062929967b4956235e3))
* **docker:** propagate child process exit codes ([be67ea8](https://github.com/nzbdav/nzbdav/commit/be67ea8974a2ff27365c4283c54fdd120732801c))
* harden security-sensitive request handling ([1e0f2dc](https://github.com/nzbdav/nzbdav/commit/1e0f2dc3ee637d8603c94da7271e1932922fd1e2))
* **health:** bound the missing segment cache ([cdbc614](https://github.com/nzbdav/nzbdav/commit/cdbc61481f202dcaf133a915ce3a14f1962d684d))
* **nntp:** close pipelined transfer lifecycle gaps ([27ccc31](https://github.com/nzbdav/nzbdav/commit/27ccc31abdc818ae95b46cc9ba326ea8e6a3c6dd))
* **queue:** start queue processing at startup ([3b6904f](https://github.com/nzbdav/nzbdav/commit/3b6904f3e51fb9be7babc5bc3ef454746b572ac9))
* **sab:** guard addurl fetches against SSRF ([6249dc4](https://github.com/nzbdav/nzbdav/commit/6249dc47152a56478cceba252684d9f8e7d56567))
* **sab:** make history delete idempotent for missing ids ([#36](https://github.com/nzbdav/nzbdav/issues/36)) ([e16b7c5](https://github.com/nzbdav/nzbdav/commit/e16b7c543e6c8c7444965798c3a693001167a5bb))
* **sab:** retry history delete save when rows vanish concurrently ([1b9bf1e](https://github.com/nzbdav/nzbdav/commit/1b9bf1e28162ccf13809474de8bd649411f5c584))
* **sab:** validate category before backup paths ([f56812a](https://github.com/nzbdav/nzbdav/commit/f56812a1dc04984ba75b261fb6b991e321da6675))
* **ui:** guard backend websocket parsing ([7e65ff9](https://github.com/nzbdav/nzbdav/commit/7e65ff90462f810439effe4fca4c2c068bd34d1b))
* **ui:** improve standalone frontend status ([b47ebde](https://github.com/nzbdav/nzbdav/commit/b47ebdef341c3f721bd1711d14d7c34f6f267d3e))
* **ui:** replace incompatible queue dropzone ([6b5dc08](https://github.com/nzbdav/nzbdav/commit/6b5dc08721a3b5f4518edaeff6470674a7c09369))
* **usenet:** skip websocket encoding without subscribers ([e837451](https://github.com/nzbdav/nzbdav/commit/e837451b6492dc19db909f541efa4a16334043c7))
* **webdav:** decrypt AES streams in 256 KiB runs ([6b4f867](https://github.com/nzbdav/nzbdav/commit/6b4f867664a7dc54298c39b269a923f552f23577))
* **webdav:** discard seek prefixes in 64 KiB chunks ([c649dea](https://github.com/nzbdav/nzbdav/commit/c649dea5720ad009771eb5455b388f7fd4532271))
* **webdav:** enforce seek and stream lifecycle contracts ([c390134](https://github.com/nzbdav/nzbdav/commit/c390134875dee5d285979cbacfc0283c29a8218d))
* **webdav:** honor construction cancellation token in CancellableStream reads ([452227b](https://github.com/nzbdav/nzbdav/commit/452227bb6c63fac6683c22d9346baf787b197ede))
* **webdav:** pool the response copy buffer ([5997260](https://github.com/nzbdav/nzbdav/commit/5997260c8abffd8f334b3dcd79d02ff67ce71895))
* **webdav:** preserve prefetch on small forward seeks ([79f14a4](https://github.com/nzbdav/nzbdav/commit/79f14a41ea00337bfa6557d49d7942331046bf0b))
* **webdav:** serve correct suffix byte ranges ([34a5530](https://github.com/nzbdav/nzbdav/commit/34a55302267ba1cee6ba82ec0d88dd564bfbaa89))
* **webdav:** surface segment producer failures ([9610dc5](https://github.com/nzbdav/nzbdav/commit/9610dc517363dedf37372cae994fd516d7b2ae59))
* **webdav:** throw when stream reads are cancelled ([5e4cb9a](https://github.com/nzbdav/nzbdav/commit/5e4cb9aedcdc86ac0a4e1d76d604f44a457a6519))

## [0.6.7](https://github.com/nzbdav/nzbdav/compare/v0.6.6...v0.6.7) (2026-07-10)


### Bug Fixes

* **ci:** quote pre-release job if for Dependabot parser ([c0f3f48](https://github.com/nzbdav/nzbdav/commit/c0f3f4800eac8b31868987df84a3ce5374c72673))
* **deps:** align react-router packages and group Dependabot updates ([71d4490](https://github.com/nzbdav/nzbdav/commit/71d4490286b2c8245bc774cc1024c36a4e083c23))
* **deps:** bump the github-actions group with 3 updates ([#33](https://github.com/nzbdav/nzbdav/issues/33)) ([f4ce334](https://github.com/nzbdav/nzbdav/commit/f4ce334cb59fd56c9d0f4edb503d48e33215ff2c))
* **deps:** Bump the nuget-minor-and-patch group with 3 updates ([#34](https://github.com/nzbdav/nzbdav/issues/34)) ([4f3864c](https://github.com/nzbdav/nzbdav/commit/4f3864c416fa9e0992d781c96bad9a1d750b27c3))
* **deps:** configure Dependabot auth for UsenetSharp NuGet feed ([0b676c2](https://github.com/nzbdav/nzbdav/commit/0b676c22723e2d4aacf6cc03090425c7c85167da))
* **docker:** publish dev tag from pre-release workflow ([409c6ee](https://github.com/nzbdav/nzbdav/commit/409c6ee12dbbfddd6df80ba4156c6e91c246bbd6))
* **docker:** use vMAJOR and vMAJOR.MINOR rolling tags ([9fd3fb6](https://github.com/nzbdav/nzbdav/commit/9fd3fb63db3c7160d5889e081d957b102b0aa1c1))

## [0.6.6](https://github.com/nzbdav/nzbdav/compare/v0.6.5...v0.6.6) (2026-07-10)


### Bug Fixes

* **ci:** add self-managed dependency submission with NuGet auth ([b35b856](https://github.com/nzbdav/nzbdav/commit/b35b856f58685ebb1eda9cfb16f15ab404d6d2b8))
* **ci:** deploy Docker images on release published event ([c9f8207](https://github.com/nzbdav/nzbdav/commit/c9f8207c95abb41f6ab413a85d0d755dcc3d3f07))
* **ci:** quote pre-release image tag for Dependabot parser ([0066a1f](https://github.com/nzbdav/nzbdav/commit/0066a1f07db64de69c7c8044fdd80b64b84f3169))
* **deps:** remove invalid GITHUB_TOKEN from Dependabot registry config ([d5bb833](https://github.com/nzbdav/nzbdav/commit/d5bb833b23683858c211e230375d3f1f415e540e))
* **ui:** emit node build output to dist-node only ([507bb66](https://github.com/nzbdav/nzbdav/commit/507bb66b60c0d810b3570133620a6a39e4397643))

## [0.6.5](https://github.com/hoivikaj/nzbdav/compare/v0.6.4...v0.6.5) (2026-07-10)


### Features

* add backend support for scheduling the RemoveOrphanedFiles task. ([ffcdcfd](https://github.com/hoivikaj/nzbdav/commit/ffcdcfd4e25d1ced7648bef29e0a3d246c419410))
* add NZB backup settings to frontend. ([55260d4](https://github.com/hoivikaj/nzbdav/commit/55260d41d00722b3881b4eeea5d5d07e86d5704b))
* add ui setting to schedule the RemoveOrphanedFiles task. ([807573b](https://github.com/hoivikaj/nzbdav/commit/807573bd7411afcb22731e9a40dc0c8f519b2f95))
* allow exporting nzb from history table. ([7928d4b](https://github.com/hoivikaj/nzbdav/commit/7928d4b1fb5fc785828b4a7b211d5c62b37b6243))
* backup incoming nzbs to configured directory when enabled. ([c2b3692](https://github.com/hoivikaj/nzbdav/commit/c2b369229ae7ebd0bd3bfaa14c99f939d93c241e))
* index QueueItems table by category and filename. ([9116bfc](https://github.com/hoivikaj/nzbdav/commit/9116bfc93407dc867206f16f644f7201591ff0e1))
* organize /nzbs webdav dir by category. ([404d418](https://github.com/hoivikaj/nzbdav/commit/404d418a8a0a9d1465c1115b87a8506a5b9d56de))
* support the TZ (timezone) env variable. ([cfe0298](https://github.com/hoivikaj/nzbdav/commit/cfe02980593b07dd7800a2ce42cdfcd1765cdd20))


### Bug Fixes

* Allow special characters for filename-passwords ([#308](https://github.com/hoivikaj/nzbdav/issues/308)) ([df8b845](https://github.com/hoivikaj/nzbdav/commit/df8b84515f7b134485c4a07143413de2c1fe2e40))
* compatability issues with NZBDonkey ([#316](https://github.com/hoivikaj/nzbdav/issues/316)) ([b2d0f2a](https://github.com/hoivikaj/nzbdav/commit/b2d0f2a4c6b48cca688bdffb91ba1b71a3fb1b84))
* **deps:** bump @tailwindcss/vite from 4.1.11 to 4.2.1 in /frontend ([#330](https://github.com/hoivikaj/nzbdav/issues/330)) ([3389627](https://github.com/hoivikaj/nzbdav/commit/3389627c98a50370d580d614ebb0f0874d507219))
* **deps:** bump @types/express-serve-static-core ([#347](https://github.com/hoivikaj/nzbdav/issues/347)) ([95f8953](https://github.com/hoivikaj/nzbdav/commit/95f89533f1ed3f16a4c862f3e67f83d6b6ddf401))
* **deps:** bump @types/node from 20.19.10 to 25.4.0 in /frontend ([#328](https://github.com/hoivikaj/nzbdav/issues/328)) ([7239021](https://github.com/hoivikaj/nzbdav/commit/72390216d65380230fff1b0c091ec677e892a223))
* **deps:** bump @types/node from 25.4.0 to 25.5.0 in /frontend ([#381](https://github.com/hoivikaj/nzbdav/issues/381)) ([680e80d](https://github.com/hoivikaj/nzbdav/commit/680e80df44d4a86a6c896e25c54762159fd69741))
* **deps:** bump actions/checkout from 4 to 6 ([#317](https://github.com/hoivikaj/nzbdav/issues/317)) ([b41042e](https://github.com/hoivikaj/nzbdav/commit/b41042ea66aeb30859674e9885f15143fe8545c7))
* **deps:** bump bootstrap from 5.3.7 to 5.3.8 in /frontend ([#329](https://github.com/hoivikaj/nzbdav/issues/329)) ([1790518](https://github.com/hoivikaj/nzbdav/commit/17905189d379ae0d8ed0e2934d3acde7e3009785))
* **deps:** bump cross-env from 7.0.3 to 10.1.0 in /frontend ([#336](https://github.com/hoivikaj/nzbdav/issues/336)) ([b8d6693](https://github.com/hoivikaj/nzbdav/commit/b8d6693225e819127bb40063f335c8ab7a4f5ca0))
* **deps:** bump docker/login-action from 3 to 4 ([#321](https://github.com/hoivikaj/nzbdav/issues/321)) ([12094ea](https://github.com/hoivikaj/nzbdav/commit/12094ea4e4797799981155ac801d9730ddf824db))
* **deps:** bump express and @types/express in /frontend ([#324](https://github.com/hoivikaj/nzbdav/issues/324)) ([1539ce5](https://github.com/hoivikaj/nzbdav/commit/1539ce5d50ac53f1ca39a65166d17ed80fb295e1))
* **deps:** bump isbot from 5.1.29 to 5.1.35 in /frontend ([#322](https://github.com/hoivikaj/nzbdav/issues/322)) ([2d0d069](https://github.com/hoivikaj/nzbdav/commit/2d0d0694ecc060134810e7c2d4bbb07aaa94a74f))
* **deps:** bump isbot from 5.1.35 to 5.1.36 in /frontend ([#349](https://github.com/hoivikaj/nzbdav/issues/349)) ([0619772](https://github.com/hoivikaj/nzbdav/commit/06197726fd2be0695027e5a7ca1ecf8c55d21586))
* **deps:** bump isbot from 5.1.36 to 5.1.37 in /frontend ([#379](https://github.com/hoivikaj/nzbdav/issues/379)) ([b054f42](https://github.com/hoivikaj/nzbdav/commit/b054f42a8e2b715f94995b5e37763f8c0d9651f7))
* **deps:** Bump Microsoft.AspNetCore.OpenApi from 10.0.1 to 10.0.4 ([#332](https://github.com/hoivikaj/nzbdav/issues/332)) ([7e0cfd6](https://github.com/hoivikaj/nzbdav/commit/7e0cfd6acada37b2b2de8961eae9d095a97f8417))
* **deps:** Bump Microsoft.EntityFrameworkCore.Design from 10.0.1 to 10.0.4 ([#334](https://github.com/hoivikaj/nzbdav/issues/334)) ([88fa597](https://github.com/hoivikaj/nzbdav/commit/88fa5976bda674e98d2bf57802fbddeb721abaaa))
* **deps:** Bump Microsoft.EntityFrameworkCore.Sqlite from 10.0.1 to 10.0.4 ([#338](https://github.com/hoivikaj/nzbdav/issues/338)) ([e19d72c](https://github.com/hoivikaj/nzbdav/commit/e19d72cd42b9ea302fc6e5dae32ea0e2652f1094))
* **deps:** bump mime-types from 3.0.1 to 3.0.2 in /frontend ([#323](https://github.com/hoivikaj/nzbdav/issues/323)) ([8866951](https://github.com/hoivikaj/nzbdav/commit/88669514ff6ff279647cd8f92f23ae9f3aa908a4))
* **deps:** bump react-dropzone from 14.3.8 to 15.0.0 in /frontend ([#348](https://github.com/hoivikaj/nzbdav/issues/348)) ([ab24e15](https://github.com/hoivikaj/nzbdav/commit/ab24e15c3b8ec3cda5c07c2943adbf1fadd1c52c))
* **deps:** bump tailwindcss from 4.1.11 to 4.2.1 in /frontend ([#335](https://github.com/hoivikaj/nzbdav/issues/335)) ([2a62a41](https://github.com/hoivikaj/nzbdav/commit/2a62a41e8b3b094f69bbb687bec775776530435b))
* **deps:** Bump the dotnet group with 3 updates ([#395](https://github.com/hoivikaj/nzbdav/issues/395)) ([aae1e43](https://github.com/hoivikaj/nzbdav/commit/aae1e4367bb70f7a0a517779f453680c1e06c2bb))
* **deps:** bump the github-actions group across 1 directory with 2 updates ([75f7dfb](https://github.com/hoivikaj/nzbdav/commit/75f7dfb16815ed25cc57dcde0c5cb615ae70ebaa))
* **deps:** bump the github-actions group across 1 directory with 2 updates ([2f4d6c3](https://github.com/hoivikaj/nzbdav/commit/2f4d6c3263a9b8f0f5218b0948d79e208e397ff4))
* **deps:** bump the github-actions group with 3 updates ([#350](https://github.com/hoivikaj/nzbdav/issues/350)) ([e017ca9](https://github.com/hoivikaj/nzbdav/commit/e017ca9d868b624b3686789772f28790a18532ee))
* **deps:** bump the react group in /frontend with 2 updates ([#394](https://github.com/hoivikaj/nzbdav/issues/394)) ([5ce46bc](https://github.com/hoivikaj/nzbdav/commit/5ce46bc74b0cf671a91987f92aca96c5830d4615))
* **deps:** bump the react group in /frontend with 4 updates ([#346](https://github.com/hoivikaj/nzbdav/issues/346)) ([46a8a7b](https://github.com/hoivikaj/nzbdav/commit/46a8a7bc605033c8bf64bc159f9337425044b292))
* **deps:** bump the react-router group in /frontend with 5 updates ([#345](https://github.com/hoivikaj/nzbdav/issues/345)) ([83833f4](https://github.com/hoivikaj/nzbdav/commit/83833f4e35cacc7010368a9b0935d1ed6945b58f))
* **deps:** bump the react-router group in /frontend with 5 updates ([#372](https://github.com/hoivikaj/nzbdav/issues/372)) ([27d4cea](https://github.com/hoivikaj/nzbdav/commit/27d4cea5790d92bc6f965e3dfdb3c50f9dad207a))
* **deps:** bump the tailwindcss group in /frontend with 2 updates ([#374](https://github.com/hoivikaj/nzbdav/issues/374)) ([2f1c0f8](https://github.com/hoivikaj/nzbdav/commit/2f1c0f8bf480d7d49dfdadcafba47e7e6f7ce948))
* **deps:** bump tsx from 4.20.3 to 4.21.0 in /frontend ([#326](https://github.com/hoivikaj/nzbdav/issues/326)) ([71974ec](https://github.com/hoivikaj/nzbdav/commit/71974eca1762fb72f5f9ecad181b33a8dacb413f))
* **deps:** bump typescript from 5.9.2 to 5.9.3 in /frontend ([#325](https://github.com/hoivikaj/nzbdav/issues/325)) ([1c692a6](https://github.com/hoivikaj/nzbdav/commit/1c692a66364cce5112f2c66bff55ec9ce400ba13))
* **deps:** bump vite from 6.3.5 to 7.3.1 in /frontend ([#337](https://github.com/hoivikaj/nzbdav/issues/337)) ([0f8eea6](https://github.com/hoivikaj/nzbdav/commit/0f8eea6db59d16a3aeaf4b611e8c6b8d94b77e00))
* **deps:** bump vite from 7.3.1 to 8.0.3 in /frontend in the vite group ([#375](https://github.com/hoivikaj/nzbdav/issues/375)) ([2efc0c2](https://github.com/hoivikaj/nzbdav/commit/2efc0c24ae2672afe5644a0186dbc1ebad710419))
* **deps:** bump vite-tsconfig-paths from 5.1.4 to 6.1.1 in /frontend ([#341](https://github.com/hoivikaj/nzbdav/issues/341)) ([c396ad3](https://github.com/hoivikaj/nzbdav/commit/c396ad34a826ea1cc37cf2d29e30466031eb79be))
* **deps:** bump ws from 8.18.3 to 8.19.0 in /frontend ([#342](https://github.com/hoivikaj/nzbdav/issues/342)) ([f2fa35d](https://github.com/hoivikaj/nzbdav/commit/f2fa35d86ad03c73ba5584ba2ccb3c28f25ef34d))
* **deps:** bump ws from 8.19.0 to 8.20.0 in /frontend ([#380](https://github.com/hoivikaj/nzbdav/issues/380)) ([cb42d73](https://github.com/hoivikaj/nzbdav/commit/cb42d73124d528b57addc70d542f562ca16d8496))
* **deps:** ran `npm audit fix`. ([a71cf69](https://github.com/hoivikaj/nzbdav/commit/a71cf694d9c4e0e7492ec357d68981982e148e52))
* **deps:** removed the vite-tsconfig-paths plugin. ([c2bdf1d](https://github.com/hoivikaj/nzbdav/commit/c2bdf1dd50f745f7929df623bc5bd0be5fff8887))
* downgrade unreachable Arr instance log level from Error to Debug ([#352](https://github.com/hoivikaj/nzbdav/issues/352)) ([90a03bf](https://github.com/hoivikaj/nzbdav/commit/90a03bf3e63a871b75d25ab109a6fcdd4689ffae))
* ensure `audio/flac` content-type mapping for flac files. ([5253fe3](https://github.com/hoivikaj/nzbdav/commit/5253fe3f03cbc2889928c338b2096acc7b863a52))
* fail queue items with missing nzb blobs instead of blocking queue ([#351](https://github.com/hoivikaj/nzbdav/issues/351)) ([a146d07](https://github.com/hoivikaj/nzbdav/commit/a146d07d8c62891993796b28ad358e41385dd02d))
* funnel frontend auth through middleware. ([eb71ebf](https://github.com/hoivikaj/nzbdav/commit/eb71ebf8432fc78446de1e37e4d9c5c3e81112be))
* improve error message for malformed nzbs. ([325252e](https://github.com/hoivikaj/nzbdav/commit/325252e65f910f36d0e52810ccb2fba0d1a50019))
* **nntp:** Skip failing usenet providers with circuit breaker ([#400](https://github.com/hoivikaj/nzbdav/issues/400)) ([c5fa860](https://github.com/hoivikaj/nzbdav/commit/c5fa860930a55b566a06a74006dcc777079f6716))
* **nntp:** tag provider name in connection-lock and command-error logs ([#441](https://github.com/hoivikaj/nzbdav/issues/441)) ([794948b](https://github.com/hoivikaj/nzbdav/commit/794948be293eaade7e495cb9ea88045ae33d699b))
* NZBDonkey compatibility issues with nzb category ([#316](https://github.com/hoivikaj/nzbdav/issues/316)) ([7059b10](https://github.com/hoivikaj/nzbdav/commit/7059b10c4fb79d3dda7c3745360cddbee3ef0561))
* remove 'Delete mounted files' option when clearing a failed history item. ([dfbc411](https://github.com/hoivikaj/nzbdav/commit/dfbc41148a0877cecba45bd01c97602222d1dac1))
* typo when disposing queue nzb stream. ([3e44aae](https://github.com/hoivikaj/nzbdav/commit/3e44aaebd635f6dcd9949f1d6dcd80d61985cbb0))
* update changelog link on ui leftnav-menu. ([14cd09d](https://github.com/hoivikaj/nzbdav/commit/14cd09d2a5f88438b79b46cc6b9c1200fedf0c16))
* updated opacity for disabled history actions. ([0b82f48](https://github.com/hoivikaj/nzbdav/commit/0b82f482465d0c7a81c3dca7889b57a9e0d060b2))
* updated padding on queue/history tables. ([2e83dc7](https://github.com/hoivikaj/nzbdav/commit/2e83dc74e75a27b3cba1aa5b82f5da5a0b1a8217))
* webdav range requests past content boundary return 500 instead 416 ([#384](https://github.com/hoivikaj/nzbdav/issues/384)) ([a43d5d7](https://github.com/hoivikaj/nzbdav/commit/a43d5d7e3d2de1201800dab1a38ad67b1e9d001e))

## [0.6.4](https://github.com/nzbdav-dev/nzbdav/compare/v0.6.3...v0.6.4) (2026-04-08)


### Bug Fixes

* **deps:** bump @types/node from 25.4.0 to 25.5.0 in /frontend ([#381](https://github.com/nzbdav-dev/nzbdav/issues/381)) ([680e80d](https://github.com/nzbdav-dev/nzbdav/commit/680e80df44d4a86a6c896e25c54762159fd69741))
* **deps:** bump isbot from 5.1.36 to 5.1.37 in /frontend ([#379](https://github.com/nzbdav-dev/nzbdav/issues/379)) ([b054f42](https://github.com/nzbdav-dev/nzbdav/commit/b054f42a8e2b715f94995b5e37763f8c0d9651f7))
* **deps:** Bump the dotnet group with 3 updates ([#395](https://github.com/nzbdav-dev/nzbdav/issues/395)) ([aae1e43](https://github.com/nzbdav-dev/nzbdav/commit/aae1e4367bb70f7a0a517779f453680c1e06c2bb))
* **deps:** bump the react group in /frontend with 2 updates ([#394](https://github.com/nzbdav-dev/nzbdav/issues/394)) ([5ce46bc](https://github.com/nzbdav-dev/nzbdav/commit/5ce46bc74b0cf671a91987f92aca96c5830d4615))
* **deps:** bump the react-router group in /frontend with 5 updates ([#372](https://github.com/nzbdav-dev/nzbdav/issues/372)) ([27d4cea](https://github.com/nzbdav-dev/nzbdav/commit/27d4cea5790d92bc6f965e3dfdb3c50f9dad207a))
* **deps:** bump the tailwindcss group in /frontend with 2 updates ([#374](https://github.com/nzbdav-dev/nzbdav/issues/374)) ([2f1c0f8](https://github.com/nzbdav-dev/nzbdav/commit/2f1c0f8bf480d7d49dfdadcafba47e7e6f7ce948))
* **deps:** bump vite from 7.3.1 to 8.0.3 in /frontend in the vite group ([#375](https://github.com/nzbdav-dev/nzbdav/issues/375)) ([2efc0c2](https://github.com/nzbdav-dev/nzbdav/commit/2efc0c24ae2672afe5644a0186dbc1ebad710419))
* **deps:** bump ws from 8.19.0 to 8.20.0 in /frontend ([#380](https://github.com/nzbdav-dev/nzbdav/issues/380)) ([cb42d73](https://github.com/nzbdav-dev/nzbdav/commit/cb42d73124d528b57addc70d542f562ca16d8496))

## [0.6.3](https://github.com/nzbdav-dev/nzbdav/compare/v0.6.2...v0.6.3) (2026-04-08)


### Features

* add NZB backup settings to frontend. ([55260d4](https://github.com/nzbdav-dev/nzbdav/commit/55260d41d00722b3881b4eeea5d5d07e86d5704b))
* allow exporting nzb from history table. ([7928d4b](https://github.com/nzbdav-dev/nzbdav/commit/7928d4b1fb5fc785828b4a7b211d5c62b37b6243))
* backup incoming nzbs to configured directory when enabled. ([c2b3692](https://github.com/nzbdav-dev/nzbdav/commit/c2b369229ae7ebd0bd3bfaa14c99f939d93c241e))
* index QueueItems table by category and filename. ([9116bfc](https://github.com/nzbdav-dev/nzbdav/commit/9116bfc93407dc867206f16f644f7201591ff0e1))
* organize /nzbs webdav dir by category. ([404d418](https://github.com/nzbdav-dev/nzbdav/commit/404d418a8a0a9d1465c1115b87a8506a5b9d56de))


### Bug Fixes

* remove 'Delete mounted files' option when clearing a failed history item. ([dfbc411](https://github.com/nzbdav-dev/nzbdav/commit/dfbc41148a0877cecba45bd01c97602222d1dac1))
* updated opacity for disabled history actions. ([0b82f48](https://github.com/nzbdav-dev/nzbdav/commit/0b82f482465d0c7a81c3dca7889b57a9e0d060b2))
* updated padding on queue/history tables. ([2e83dc7](https://github.com/nzbdav-dev/nzbdav/commit/2e83dc74e75a27b3cba1aa5b82f5da5a0b1a8217))
* webdav range requests past content boundary return 500 instead 416 ([#384](https://github.com/nzbdav-dev/nzbdav/issues/384)) ([a43d5d7](https://github.com/nzbdav-dev/nzbdav/commit/a43d5d7e3d2de1201800dab1a38ad67b1e9d001e))

## [0.6.2](https://github.com/nzbdav-dev/nzbdav/compare/v0.6.1...v0.6.2) (2026-03-24)


### Bug Fixes

* compatability issues with NZBDonkey ([#316](https://github.com/nzbdav-dev/nzbdav/issues/316)) ([b2d0f2a](https://github.com/nzbdav-dev/nzbdav/commit/b2d0f2a4c6b48cca688bdffb91ba1b71a3fb1b84))
* downgrade unreachable Arr instance log level from Error to Debug ([#352](https://github.com/nzbdav-dev/nzbdav/issues/352)) ([90a03bf](https://github.com/nzbdav-dev/nzbdav/commit/90a03bf3e63a871b75d25ab109a6fcdd4689ffae))
* ensure `audio/flac` content-type mapping for flac files. ([5253fe3](https://github.com/nzbdav-dev/nzbdav/commit/5253fe3f03cbc2889928c338b2096acc7b863a52))
* fail queue items with missing nzb blobs instead of blocking queue ([#351](https://github.com/nzbdav-dev/nzbdav/issues/351)) ([a146d07](https://github.com/nzbdav-dev/nzbdav/commit/a146d07d8c62891993796b28ad358e41385dd02d))
* funnel frontend auth through middleware. ([eb71ebf](https://github.com/nzbdav-dev/nzbdav/commit/eb71ebf8432fc78446de1e37e4d9c5c3e81112be))
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
