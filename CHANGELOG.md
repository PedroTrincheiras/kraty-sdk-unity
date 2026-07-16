# Changelog

All notable changes to `app.kraty.sdk` (Kraty Unity SDK) live here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) +
[SemVer](https://semver.org/).

## [0.13.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.12.0...sdk-client-unity-v0.13.0) (2026-07-16)


### ⚠ BREAKING CHANGES

* **sdk:** the finalization MembershipKind wire/persisted values are now 'leaderboard' and 'event_leaderboard' (were 'shared_board' / 'event_board'). Client apps that persisted membership refs across an upgrade should clear them.
* **leaderboards:** the cross-event ("shared") leaderboard id in SDK/API responses is now `leaderboardId` (was `sharedLeaderboardId`). Physical DB tables were renamed; migration 0012 must run.
* **api:** API responses and SDK types now use `avatar` instead of `avatarUrl` for leaderboard entries and the player synthetic identity. Client code reading `entry.avatarUrl` or `syntheticIdentity.avatarUrl` must switch to `avatar`.
* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID)
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json

### Features

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split ([53897ca](https://github.com/PedroTrincheiras/Kraty/commit/53897ca4cc245569dc86353de72fda3df8b925b9))
* **api:** leaderboard join + flexible multi-segment standings ([7a9f11f](https://github.com/PedroTrincheiras/Kraty/commit/7a9f11f2598fa6bed8c6036863172b666e1ebce4))
* **api:** rename identity/leaderboard avatarUrl field to avatar ([76971be](https://github.com/PedroTrincheiras/Kraty/commit/76971be73e6313d8b54c1a947c1b82557885a702))
* finish an attempt now (player "end my run") across API + all SDKs ([f1ba2a0](https://github.com/PedroTrincheiras/Kraty/commit/f1ba2a0fa56a51a513bdbc865dd4ea9d854a8a65))
* **leaderboards:** server-side country on join + country on entries/register (flags) ([666c505](https://github.com/PedroTrincheiras/Kraty/commit/666c5051211efef1de97b1917e43f78327959581))
* **players:** client-SDK self-service identity change ([d73e2b9](https://github.com/PedroTrincheiras/Kraty/commit/d73e2b95a4a4a17447bfb90fe2a950b1afb599e5))
* **sdk-unity:** v0.3.3 — ReadSharedAsync for configurable leaderboards ([7d3e371](https://github.com/PedroTrincheiras/Kraty/commit/7d3e371cdc2deafa487a17e8fbba95b4a444cdf6))
* **sdk-unity:** v0.3.4 — SDK Smoke Rig sample ([283b345](https://github.com/PedroTrincheiras/Kraty/commit/283b34533060aec26b42f8103c9d81ceb983f152))
* **sdks:** document leaderboard join in Unity sample + spec `joined` field ([1719c6b](https://github.com/PedroTrincheiras/Kraty/commit/1719c6b533a748b262012e7e4f8a48c33244dc35))
* **sdks:** leaderboard submitScore (client TS/Unity/Flutter) + server scoring/progress ([02f4e69](https://github.com/PedroTrincheiras/Kraty/commit/02f4e6944813639ca19349ff1a9c28c92e6aa62f))
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID) ([96116ac](https://github.com/PedroTrincheiras/Kraty/commit/96116acf7eb90ff32f2f5e9e7cf5617dc7610ec7))
* **sdk:** subscribe() helper + lazy-eval publishes bot deltas ([099d15d](https://github.com/PedroTrincheiras/Kraty/commit/099d15dc959c0619210319d343dc380c8b74c02d))
* **sdks:** Unity + Flutter identity split; docs cover getIdentity/getAnonymizedIdentity ([9c73521](https://github.com/PedroTrincheiras/Kraty/commit/9c73521158ccbbf71be44142b643e3a4fad963ee))
* session events — sudden-death sessions, convergence, promotion/relegation ([#5](https://github.com/PedroTrincheiras/Kraty/issues/5)) ([c3d698f](https://github.com/PedroTrincheiras/Kraty/commit/c3d698f8f73816a63a9ae799841d8d8eef3e1d4f))


### Bug Fixes

* **backend:** lobby fill gap + bot kind TTL race + leaderboard isSelf flag ([4122793](https://github.com/PedroTrincheiras/Kraty/commit/4122793ee5ac02b10de88f74fea244dd2d6d650c))
* **ci:** repair all workflows after SDK restructure to client/server/ ([85f0524](https://github.com/PedroTrincheiras/Kraty/commit/85f0524ef1777240e04d96dd34dee3ec37ed7315))
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json ([5a4df35](https://github.com/PedroTrincheiras/Kraty/commit/5a4df356754f8181da80e152812659ae80ffad5d))


### Documentation

* rewrite root README + point every SDK README at kraty.io/docs ([0bb9b13](https://github.com/PedroTrincheiras/Kraty/commit/0bb9b1385ef8803aaf2f67a3a63ea746ca4b6e12))
* **sdks:** bump client SDK install snippets + READMEs to v0.12.0 ([7e43418](https://github.com/PedroTrincheiras/Kraty/commit/7e434183903a14d18ebc04b0dfecbe39caace10a))
* **sdks:** bump install snippets + READMEs for the v0.11.0 / v0.8.0 release ([8cafc26](https://github.com/PedroTrincheiras/Kraty/commit/8cafc26a6d7618c700b9177bedb6471d29efcfc7))
* **sdks:** bump install snippets + READMEs to match the release-please tag versions ([c66ffaf](https://github.com/PedroTrincheiras/Kraty/commit/c66ffaf27e1ac495957067003a411df8857a1c53))
* **sdks:** bump SCHEMA.md headers to v0.4.1 + add wire-endpoint refs ([c2bc5eb](https://github.com/PedroTrincheiras/Kraty/commit/c2bc5eb24afecf61bb22cec0be9a234615a25877))
* **sdks:** bump SCHEMA.md to v0.6.0 + document join + standings ([ccb57c3](https://github.com/PedroTrincheiras/Kraty/commit/ccb57c38bb4954277cf86780b7afd172f889c21c))


### Refactors

* **leaderboards:** rename shared leaderboards to "leaderboards", event boards to "event leaderboards" ([ec27f8e](https://github.com/PedroTrincheiras/Kraty/commit/ec27f8ecbc83c8381ec732e3540dbfc6099f99e5))
* **sdk:** rename finalization board kinds to leaderboard terminology ([cd1c007](https://github.com/PedroTrincheiras/Kraty/commit/cd1c007c2ba71b81de3ef64168bd7a847aab890d))

## [0.12.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.11.0...sdk-client-unity-v0.12.0) (2026-07-15)


### Features

* **players:** client-SDK self-service identity change ([d73e2b9](https://github.com/PedroTrincheiras/Kraty/commit/d73e2b95a4a4a17447bfb90fe2a950b1afb599e5))

## [0.11.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.10.0...sdk-client-unity-v0.11.0) (2026-07-15)


### Features

* **leaderboards:** server-side country on join + country on entries/register (flags) ([666c505](https://github.com/PedroTrincheiras/Kraty/commit/666c5051211efef1de97b1917e43f78327959581))


### Documentation

* **sdks:** bump install snippets + READMEs for the v0.11.0 / v0.8.0 release ([8cafc26](https://github.com/PedroTrincheiras/Kraty/commit/8cafc26a6d7618c700b9177bedb6471d29efcfc7))
* **sdks:** bump install snippets + READMEs to match the release-please tag versions ([c66ffaf](https://github.com/PedroTrincheiras/Kraty/commit/c66ffaf27e1ac495957067003a411df8857a1c53))

## [0.10.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.9.0...sdk-client-unity-v0.10.0) (2026-07-12)


### ⚠ BREAKING CHANGES

* **sdk:** the finalization MembershipKind wire/persisted values are now 'leaderboard' and 'event_leaderboard' (were 'shared_board' / 'event_board'). Client apps that persisted membership refs across an upgrade should clear them.
* **leaderboards:** the cross-event ("shared") leaderboard id in SDK/API responses is now `leaderboardId` (was `sharedLeaderboardId`). Physical DB tables were renamed; migration 0012 must run.
* **api:** API responses and SDK types now use `avatar` instead of `avatarUrl` for leaderboard entries and the player synthetic identity. Client code reading `entry.avatarUrl` or `syntheticIdentity.avatarUrl` must switch to `avatar`.
* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID)
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json

### Features

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split ([53897ca](https://github.com/PedroTrincheiras/Kraty/commit/53897ca4cc245569dc86353de72fda3df8b925b9))
* **api:** leaderboard join + flexible multi-segment standings ([7a9f11f](https://github.com/PedroTrincheiras/Kraty/commit/7a9f11f2598fa6bed8c6036863172b666e1ebce4))
* **api:** rename identity/leaderboard avatarUrl field to avatar ([76971be](https://github.com/PedroTrincheiras/Kraty/commit/76971be73e6313d8b54c1a947c1b82557885a702))
* finish an attempt now (player "end my run") across API + all SDKs ([f1ba2a0](https://github.com/PedroTrincheiras/Kraty/commit/f1ba2a0fa56a51a513bdbc865dd4ea9d854a8a65))
* **sdk-unity:** v0.3.3 — ReadSharedAsync for configurable leaderboards ([7d3e371](https://github.com/PedroTrincheiras/Kraty/commit/7d3e371cdc2deafa487a17e8fbba95b4a444cdf6))
* **sdk-unity:** v0.3.4 — SDK Smoke Rig sample ([283b345](https://github.com/PedroTrincheiras/Kraty/commit/283b34533060aec26b42f8103c9d81ceb983f152))
* **sdks:** document leaderboard join in Unity sample + spec `joined` field ([1719c6b](https://github.com/PedroTrincheiras/Kraty/commit/1719c6b533a748b262012e7e4f8a48c33244dc35))
* **sdks:** leaderboard submitScore (client TS/Unity/Flutter) + server scoring/progress ([02f4e69](https://github.com/PedroTrincheiras/Kraty/commit/02f4e6944813639ca19349ff1a9c28c92e6aa62f))
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID) ([96116ac](https://github.com/PedroTrincheiras/Kraty/commit/96116acf7eb90ff32f2f5e9e7cf5617dc7610ec7))
* **sdk:** subscribe() helper + lazy-eval publishes bot deltas ([099d15d](https://github.com/PedroTrincheiras/Kraty/commit/099d15dc959c0619210319d343dc380c8b74c02d))
* session events — sudden-death sessions, convergence, promotion/relegation ([#5](https://github.com/PedroTrincheiras/Kraty/issues/5)) ([c3d698f](https://github.com/PedroTrincheiras/Kraty/commit/c3d698f8f73816a63a9ae799841d8d8eef3e1d4f))


### Bug Fixes

* **backend:** lobby fill gap + bot kind TTL race + leaderboard isSelf flag ([4122793](https://github.com/PedroTrincheiras/Kraty/commit/4122793ee5ac02b10de88f74fea244dd2d6d650c))
* **ci:** repair all workflows after SDK restructure to client/server/ ([85f0524](https://github.com/PedroTrincheiras/Kraty/commit/85f0524ef1777240e04d96dd34dee3ec37ed7315))
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json ([5a4df35](https://github.com/PedroTrincheiras/Kraty/commit/5a4df356754f8181da80e152812659ae80ffad5d))


### Documentation

* rewrite root README + point every SDK README at kraty.io/docs ([0bb9b13](https://github.com/PedroTrincheiras/Kraty/commit/0bb9b1385ef8803aaf2f67a3a63ea746ca4b6e12))
* **sdks:** bump SCHEMA.md headers to v0.4.1 + add wire-endpoint refs ([c2bc5eb](https://github.com/PedroTrincheiras/Kraty/commit/c2bc5eb24afecf61bb22cec0be9a234615a25877))
* **sdks:** bump SCHEMA.md to v0.6.0 + document join + standings ([ccb57c3](https://github.com/PedroTrincheiras/Kraty/commit/ccb57c38bb4954277cf86780b7afd172f889c21c))


### Refactors

* **leaderboards:** rename shared leaderboards to "leaderboards", event boards to "event leaderboards" ([ec27f8e](https://github.com/PedroTrincheiras/Kraty/commit/ec27f8ecbc83c8381ec732e3540dbfc6099f99e5))
* **sdk:** rename finalization board kinds to leaderboard terminology ([cd1c007](https://github.com/PedroTrincheiras/Kraty/commit/cd1c007c2ba71b81de3ef64168bd7a847aab890d))

## [0.8.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.7.0...sdk-client-unity-v0.8.0) (2026-07-09)


### ⚠ BREAKING CHANGES

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID)
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json

### Features

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split ([53897ca](https://github.com/PedroTrincheiras/Kraty/commit/53897ca4cc245569dc86353de72fda3df8b925b9))
* **api:** leaderboard join + flexible multi-segment standings ([7a9f11f](https://github.com/PedroTrincheiras/Kraty/commit/7a9f11f2598fa6bed8c6036863172b666e1ebce4))
* **sdk-unity:** v0.3.3 — ReadSharedAsync for configurable leaderboards ([7d3e371](https://github.com/PedroTrincheiras/Kraty/commit/7d3e371cdc2deafa487a17e8fbba95b4a444cdf6))
* **sdk-unity:** v0.3.4 — SDK Smoke Rig sample ([283b345](https://github.com/PedroTrincheiras/Kraty/commit/283b34533060aec26b42f8103c9d81ceb983f152))
* **sdks:** document leaderboard join in Unity sample + spec `joined` field ([1719c6b](https://github.com/PedroTrincheiras/Kraty/commit/1719c6b533a748b262012e7e4f8a48c33244dc35))
* **sdks:** leaderboard submitScore (client TS/Unity/Flutter) + server scoring/progress ([02f4e69](https://github.com/PedroTrincheiras/Kraty/commit/02f4e6944813639ca19349ff1a9c28c92e6aa62f))
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID) ([96116ac](https://github.com/PedroTrincheiras/Kraty/commit/96116acf7eb90ff32f2f5e9e7cf5617dc7610ec7))
* **sdk:** subscribe() helper + lazy-eval publishes bot deltas ([099d15d](https://github.com/PedroTrincheiras/Kraty/commit/099d15dc959c0619210319d343dc380c8b74c02d))
* session events — sudden-death sessions, convergence, promotion/relegation ([#5](https://github.com/PedroTrincheiras/Kraty/issues/5)) ([c3d698f](https://github.com/PedroTrincheiras/Kraty/commit/c3d698f8f73816a63a9ae799841d8d8eef3e1d4f))


### Bug Fixes

* **backend:** lobby fill gap + bot kind TTL race + leaderboard isSelf flag ([4122793](https://github.com/PedroTrincheiras/Kraty/commit/4122793ee5ac02b10de88f74fea244dd2d6d650c))
* **ci:** repair all workflows after SDK restructure to client/server/ ([85f0524](https://github.com/PedroTrincheiras/Kraty/commit/85f0524ef1777240e04d96dd34dee3ec37ed7315))
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json ([5a4df35](https://github.com/PedroTrincheiras/Kraty/commit/5a4df356754f8181da80e152812659ae80ffad5d))


### Documentation

* rewrite root README + point every SDK README at kraty.io/docs ([0bb9b13](https://github.com/PedroTrincheiras/Kraty/commit/0bb9b1385ef8803aaf2f67a3a63ea746ca4b6e12))
* **sdks:** bump SCHEMA.md headers to v0.4.1 + add wire-endpoint refs ([c2bc5eb](https://github.com/PedroTrincheiras/Kraty/commit/c2bc5eb24afecf61bb22cec0be9a234615a25877))
* **sdks:** bump SCHEMA.md to v0.6.0 + document join + standings ([ccb57c3](https://github.com/PedroTrincheiras/Kraty/commit/ccb57c38bb4954277cf86780b7afd172f889c21c))

## [0.7.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.6.0...sdk-client-unity-v0.7.0) (2026-07-06)


### ⚠ BREAKING CHANGES

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID)
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json

### Features

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split ([53897ca](https://github.com/PedroTrincheiras/Kraty/commit/53897ca4cc245569dc86353de72fda3df8b925b9))
* **api:** leaderboard join + flexible multi-segment standings ([7a9f11f](https://github.com/PedroTrincheiras/Kraty/commit/7a9f11f2598fa6bed8c6036863172b666e1ebce4))
* **sdk-unity:** v0.3.3 — ReadSharedAsync for configurable leaderboards ([7d3e371](https://github.com/PedroTrincheiras/Kraty/commit/7d3e371cdc2deafa487a17e8fbba95b4a444cdf6))
* **sdk-unity:** v0.3.4 — SDK Smoke Rig sample ([283b345](https://github.com/PedroTrincheiras/Kraty/commit/283b34533060aec26b42f8103c9d81ceb983f152))
* **sdks:** document leaderboard join in Unity sample + spec `joined` field ([1719c6b](https://github.com/PedroTrincheiras/Kraty/commit/1719c6b533a748b262012e7e4f8a48c33244dc35))
* **sdks:** leaderboard submitScore (client TS/Unity/Flutter) + server scoring/progress ([02f4e69](https://github.com/PedroTrincheiras/Kraty/commit/02f4e6944813639ca19349ff1a9c28c92e6aa62f))
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID) ([96116ac](https://github.com/PedroTrincheiras/Kraty/commit/96116acf7eb90ff32f2f5e9e7cf5617dc7610ec7))
* **sdk:** subscribe() helper + lazy-eval publishes bot deltas ([099d15d](https://github.com/PedroTrincheiras/Kraty/commit/099d15dc959c0619210319d343dc380c8b74c02d))
* session events — sudden-death sessions, convergence, promotion/relegation ([#5](https://github.com/PedroTrincheiras/Kraty/issues/5)) ([c3d698f](https://github.com/PedroTrincheiras/Kraty/commit/c3d698f8f73816a63a9ae799841d8d8eef3e1d4f))


### Bug Fixes

* **backend:** lobby fill gap + bot kind TTL race + leaderboard isSelf flag ([4122793](https://github.com/PedroTrincheiras/Kraty/commit/4122793ee5ac02b10de88f74fea244dd2d6d650c))
* **ci:** repair all workflows after SDK restructure to client/server/ ([85f0524](https://github.com/PedroTrincheiras/Kraty/commit/85f0524ef1777240e04d96dd34dee3ec37ed7315))
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json ([5a4df35](https://github.com/PedroTrincheiras/Kraty/commit/5a4df356754f8181da80e152812659ae80ffad5d))


### Documentation

* rewrite root README + point every SDK README at kraty.io/docs ([0bb9b13](https://github.com/PedroTrincheiras/Kraty/commit/0bb9b1385ef8803aaf2f67a3a63ea746ca4b6e12))
* **sdks:** bump SCHEMA.md headers to v0.4.1 + add wire-endpoint refs ([c2bc5eb](https://github.com/PedroTrincheiras/Kraty/commit/c2bc5eb24afecf61bb22cec0be9a234615a25877))
* **sdks:** bump SCHEMA.md to v0.6.0 + document join + standings ([ccb57c3](https://github.com/PedroTrincheiras/Kraty/commit/ccb57c38bb4954277cf86780b7afd172f889c21c))

## [0.6.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.5.0...sdk-client-unity-v0.6.0) (2026-07-02)


### ⚠ BREAKING CHANGES

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID)
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json

### Features

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split ([53897ca](https://github.com/PedroTrincheiras/Kraty/commit/53897ca4cc245569dc86353de72fda3df8b925b9))
* **api:** leaderboard join + flexible multi-segment standings ([7a9f11f](https://github.com/PedroTrincheiras/Kraty/commit/7a9f11f2598fa6bed8c6036863172b666e1ebce4))
* **sdk-unity:** v0.3.3 — ReadSharedAsync for configurable leaderboards ([7d3e371](https://github.com/PedroTrincheiras/Kraty/commit/7d3e371cdc2deafa487a17e8fbba95b4a444cdf6))
* **sdk-unity:** v0.3.4 — SDK Smoke Rig sample ([283b345](https://github.com/PedroTrincheiras/Kraty/commit/283b34533060aec26b42f8103c9d81ceb983f152))
* **sdks:** document leaderboard join in Unity sample + spec `joined` field ([1719c6b](https://github.com/PedroTrincheiras/Kraty/commit/1719c6b533a748b262012e7e4f8a48c33244dc35))
* **sdks:** leaderboard submitScore (client TS/Unity/Flutter) + server scoring/progress ([02f4e69](https://github.com/PedroTrincheiras/Kraty/commit/02f4e6944813639ca19349ff1a9c28c92e6aa62f))
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID) ([96116ac](https://github.com/PedroTrincheiras/Kraty/commit/96116acf7eb90ff32f2f5e9e7cf5617dc7610ec7))
* **sdk:** subscribe() helper + lazy-eval publishes bot deltas ([099d15d](https://github.com/PedroTrincheiras/Kraty/commit/099d15dc959c0619210319d343dc380c8b74c02d))


### Bug Fixes

* **backend:** lobby fill gap + bot kind TTL race + leaderboard isSelf flag ([4122793](https://github.com/PedroTrincheiras/Kraty/commit/4122793ee5ac02b10de88f74fea244dd2d6d650c))
* **ci:** repair all workflows after SDK restructure to client/server/ ([85f0524](https://github.com/PedroTrincheiras/Kraty/commit/85f0524ef1777240e04d96dd34dee3ec37ed7315))
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json ([5a4df35](https://github.com/PedroTrincheiras/Kraty/commit/5a4df356754f8181da80e152812659ae80ffad5d))


### Documentation

* rewrite root README + point every SDK README at kraty.io/docs ([0bb9b13](https://github.com/PedroTrincheiras/Kraty/commit/0bb9b1385ef8803aaf2f67a3a63ea746ca4b6e12))
* **sdks:** bump SCHEMA.md headers to v0.4.1 + add wire-endpoint refs ([c2bc5eb](https://github.com/PedroTrincheiras/Kraty/commit/c2bc5eb24afecf61bb22cec0be9a234615a25877))
* **sdks:** bump SCHEMA.md to v0.6.0 + document join + standings ([ccb57c3](https://github.com/PedroTrincheiras/Kraty/commit/ccb57c38bb4954277cf86780b7afd172f889c21c))

## [0.5.0](https://github.com/PedroTrincheiras/Kraty/compare/sdk-client-unity-v0.1.0...sdk-client-unity-v0.5.0) (2026-06-29)


### ⚠ BREAKING CHANGES

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID)
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json

### Features

* **api,sdks:** rename leaderboard URLs to match v0.4.0 client split ([53897ca](https://github.com/PedroTrincheiras/Kraty/commit/53897ca4cc245569dc86353de72fda3df8b925b9))
* **sdk-unity:** v0.3.3 — ReadSharedAsync for configurable leaderboards ([7d3e371](https://github.com/PedroTrincheiras/Kraty/commit/7d3e371cdc2deafa487a17e8fbba95b4a444cdf6))
* **sdk-unity:** v0.3.4 — SDK Smoke Rig sample ([283b345](https://github.com/PedroTrincheiras/Kraty/commit/283b34533060aec26b42f8103c9d81ceb983f152))
* **sdks:** leaderboard submitScore (client TS/Unity/Flutter) + server scoring/progress ([02f4e69](https://github.com/PedroTrincheiras/Kraty/commit/02f4e6944813639ca19349ff1a9c28c92e6aa62f))
* **sdks:** split Leaderboards into Leaderboards (by key) + EventLeaderboards (by UUID) ([96116ac](https://github.com/PedroTrincheiras/Kraty/commit/96116acf7eb90ff32f2f5e9e7cf5617dc7610ec7))
* **sdk:** subscribe() helper + lazy-eval publishes bot deltas ([099d15d](https://github.com/PedroTrincheiras/Kraty/commit/099d15dc959c0619210319d343dc380c8b74c02d))


### Bug Fixes

* **backend:** lobby fill gap + bot kind TTL race + leaderboard isSelf flag ([4122793](https://github.com/PedroTrincheiras/Kraty/commit/4122793ee5ac02b10de88f74fea244dd2d6d650c))
* **ci:** repair all workflows after SDK restructure to client/server/ ([85f0524](https://github.com/PedroTrincheiras/Kraty/commit/85f0524ef1777240e04d96dd34dee3ec37ed7315))
* **sdk-unity:** port from System.Text.Json to Newtonsoft.Json ([5a4df35](https://github.com/PedroTrincheiras/Kraty/commit/5a4df356754f8181da80e152812659ae80ffad5d))


### Documentation

* rewrite root README + point every SDK README at kraty.io/docs ([0bb9b13](https://github.com/PedroTrincheiras/Kraty/commit/0bb9b1385ef8803aaf2f67a3a63ea746ca4b6e12))
* **sdks:** bump SCHEMA.md headers to v0.4.1 + add wire-endpoint refs ([c2bc5eb](https://github.com/PedroTrincheiras/Kraty/commit/c2bc5eb24afecf61bb22cec0be9a234615a25877))

## [Unreleased]

### Added

- **`LeaderboardsClient.SubmitScoreAsync(key, value, opts?, ct?)`** —
  submit a score for the active player directly to a
  dashboard-configured board, outside an event attempt. Wraps
  `POST /sdk/v1/players/:externalId/leaderboards/:key/score`. Returns
  a new `LeaderboardScoreResult` (`LeaderboardId` / `Score` / `Rank`,
  where `Rank` is `int?`). New `LeaderboardSubmitOptions`
  (`Segment` / `IdempotencyKey` / `ExternalId`). Errors:
  `client_scoring_disabled` (403), `score_not_supported` (400),
  `not_found` (404), `validation_failed` (400).

### Changed

- `LeaderboardsClient.ReadAsync` — `LeaderboardReadOptions.Segment` is
  now required only for `context` segmentation. For
  `progression`-segmented boards leave it null and the server derives
  the caller's division. Signature unchanged.

## [0.4.1] — 2026-06-26

### Changed (BREAKING URL alignment with v0.4.0 rename)

- **Backend URLs renamed in lockstep with the SDK clients.** The
  v0.4.0 split (`Leaderboards` by key, `EventLeaderboards` by UUID)
  is now mirrored on the wire:
  - `GET /sdk/v1/leaderboards/:key`
    (was `GET /sdk/v1/shared-leaderboards/:key`)
  - `GET /sdk/v1/leaderboards/:key/periods`
    (was `GET /sdk/v1/shared-leaderboards/:key/periods`)
  - `GET /sdk/v1/event-leaderboards/:id`
    (was `GET /sdk/v1/leaderboards/:id`)
  - `GET /sdk/v1/event-leaderboards/:id/stream`
    (was `GET /sdk/v1/leaderboards/:id/stream`)
- v0.4.0's method names stay the same — only the path strings the
  SDK sends changed. Upgrading is a manifest bump.
- v0.3.x and v0.4.0 clients will 404 on the old paths after the
  backend deploy.

## [0.4.0] — 2026-06-26

### Changed (BREAKING)

- **Leaderboard naming reshaped.** The "shared" / "cross-event"
  configurable boards are now the primary `Leaderboards` surface,
  and the per-event-window boards moved under a new
  `EventLeaderboards` resource. The previous shape conflated the
  two and forced every game to know what "shared" meant.
  Migration:
  - `kraty.Leaderboards.ReadAsync(uuid, LeaderboardReadOptions)`
    → `kraty.EventLeaderboards.ReadAsync(uuid, EventLeaderboardReadOptions)`
  - `kraty.Leaderboards.LiveAsync(uuid)`
    → `kraty.EventLeaderboards.LiveAsync(uuid)`
  - `kraty.Leaderboards.Subscribe(uuid, …)`
    → `kraty.EventLeaderboards.Subscribe(uuid, …)`
  - `kraty.Leaderboards.ReadSharedAsync(key, SharedLeaderboardReadOptions)`
    → `kraty.Leaderboards.ReadAsync(key, LeaderboardReadOptions)`
  - `kraty.Leaderboards.ListSharedPeriodsAsync(key)`
    → `kraty.Leaderboards.ListPeriodsAsync(key)`
- **Types renamed in lockstep:**
  - `SharedLeaderboard` → `Leaderboard`
  - `SharedLeaderboardPeriod(s)` → `LeaderboardPeriod(s)`
  - `SharedLeaderboardReadOptions` → `LeaderboardReadOptions`
  - `Leaderboard` (old per-event shape) → `EventLeaderboard`
  - `LeaderboardReadOptions` (old per-event shape) → `EventLeaderboardReadOptions`
  - `Leaderboard.SharedLeaderboardId` JSON property still wires
    from the backend's `sharedLeaderboardId` field; the C# property
    is now just `LeaderboardId` (mirroring the new top-level type
    name).
- Backend URLs are unchanged (`/sdk/v1/leaderboards/:uuid` +
  `/sdk/v1/shared-leaderboards/:key`); this is purely an SDK-side
  rename.

## [0.3.4] — 2026-06-26

### Added

- **`SDK Smoke Rig` sample.** A single-file `MonoBehaviour` panel
  importable via Package Manager → Samples that exercises every
  public surface of the SDK (Identity / Events / Leaderboards
  per-event + shared / Grants / Lobbies / Inventory / Wallet)
  against a configured backend, with a `Run ALL` button for one-shot
  smoke-testing. IMGUI-only so no scene, prefab, or UI Toolkit
  dependency. The point is catching regressions that only manifest
  in shipped Player builds (IL2CPP stripping, off-thread
  PlayerPrefs, missing meta files) — bugs editor unit tests miss
  by definition. Each release should be sanity-checked through this
  rig in both Editor and a real Player build before publish.

## [0.3.3] — 2026-06-26

### Added

- **`Leaderboards.ReadSharedAsync(key, opts?, ct?)`** — snapshot read for
  configurable cross-event leaderboards addressed by their game-scoped
  key (e.g. `"weekly_global"`). Hits `GET /sdk/v1/shared-leaderboards/:key`.
  Use this for any board defined in the dashboard's Leaderboards page;
  reserve `ReadAsync` for the auto-created per-event-window boards
  addressed by UUID. Supports `Limit`, `Segment`, `Period`
  (`"current"` or an ISO timestamp from `ListSharedPeriodsAsync`),
  and `IncludeSelf` + `ExternalId`. Passing the key into the old
  `ReadAsync` previously crashed the server with a uuid-syntax 500;
  the backend now returns a clear 400 pointing at this method.
- **`Leaderboards.ListSharedPeriodsAsync(key, limit?, ct?)`** — newest-first
  list of finalized snapshot periods for a shared board. Pair with
  `ReadSharedAsync(key, opts.Period = period.PeriodStartedAt)` to render
  "last week's top 10" UI.
- New DTOs: `SharedLeaderboard`, `SharedLeaderboardPeriod`,
  `SharedLeaderboardPeriods`, `SharedLeaderboardReadOptions`.

## [0.3.2] — 2026-06-25

### Fixed

- **`PlayerPrefsSecretStore` is now main-thread-safe.** UnityEngine's
  `PlayerPrefs` throws when read or written off the player loop
  (`can only be called from the main thread`), and the SDK's
  identity resolver awaits its network I/O with
  `ConfigureAwait(false)` — the continuations that touched
  `PlayerPrefs` were landing on thread-pool threads in shipped
  builds and crashing the auth bootstrap. The store now captures
  the player-loop `SynchronizationContext` at construction and
  marshals every read/write/remove back onto it via a small
  `OnMainThread(...)` helper, returning a `Task` that completes
  when the main thread finishes the prefs op. Inline fast-path
  when the caller is already on the main thread keeps the cost
  to one allocation per access (the wrapper `Task`).

## [0.3.1] — 2026-06-16

### Fixed

- **Shipped `.meta` files for every package asset.** The package's
  `.gitignore` carried a blanket `*.meta` rule, so the published UPM
  package contained no meta files. Unity then generated fresh random
  GUIDs on each consumer's import, breaking the assembly-definition
  reference and emitting import errors on the SDK scripts. The
  Runtime scripts, folders, the asmdef, `package.json`, README and
  CHANGELOG now ship tracked metas with stable GUIDs.

## [0.3.0] — 2026-06-15

### Changed (BREAKING)

- **JSON layer ported from `System.Text.Json` to Newtonsoft.Json**
  (Json.NET) so the package actually works in Unity. Unity does not
  ship `System.Text.Json`, and `System.Text.Json`'s reflection path
  is stripped by IL2CPP on iOS / Android / WebGL — the previous SDK
  compiled in the editor but crashed on first deserialize in a
  shipped build.
- `package.json` now declares a dependency on
  `com.unity.nuget.newtonsoft-json` (3.2.1) — UPM auto-installs it
  on first import. The runtime asmdef references
  `Unity.Nuget.Newtonsoft.Json`.
- Public API types previously typed as `System.Text.Json.JsonElement`
  (the LocalizedString-ish `Name` / `Description` fields on
  `EventListing`, `CatalogItem`, `CatalogCurrency`,
  `RewardBundlePreview`; the free-form `Metadata` / `Attributes` /
  `Parameters` / `EntryRequirement` bags) now use
  `Newtonsoft.Json.Linq.JToken`. **Migration:** swap
  `value.GetString()` → `(string?)value`, `.GetInt32()` →
  `(int)value`, `.GetBoolean()` → `(bool)value`, or use
  `value.Value<T>()` / `value.ToObject<T>()` for richer
  conversions. The localized `Name` fields are now declared
  `JToken?` because the wire format may legitimately omit them.
- Internal `KratyClient.JsonSerializerOptions` getter renamed to
  `JsonSerializerSettings` and returns Newtonsoft's
  `JsonSerializerSettings` type — only consumed by the SSE stream
  helper, not in the public API surface.

### Added

- `ISecretStore` interface for per-player secret persistence, plus
  `InMemorySecretStore` (tests) and `PlayerPrefsSecretStore`
  (Unity-only, wraps `UnityEngine.PlayerPrefs`).
- `Kraty.ConnectAsPlayerAsync(...)` factory — one-call player
  bootstrap: reads the cached secret if present, otherwise calls
  `Players.RegisterAsync`, auto-rotates on 409 in dev/test envs,
  and returns a `Kraty` wired with `X-Player-Secret` on every
  request.
- `PlayersClient.RegisterAsync(externalId, force, ct)` —
  zero-trust per-player registration.
- `InventoryClient` — `ListAsync`, `ConsumeAsync` for
  platform-managed inventory.
- `WalletClient` — `ListAsync`, `DebitAsync` for platform-managed
  wallets (credit is server-API only).
- `GrantsClient.CollectAllAsync(externalId, ct)` — burn down the
  pending-grants queue in one call; per-grant failures are
  captured in `CollectAllResult.Failures` without aborting the
  sweep.
- `LeaderboardsClient.LiveAsync(id, ct)` — Server-Sent Events
  subscription with `OnEvent` / `OnError` callbacks and
  `CancelAsync`.
- `KratyClientOptions.PlayerSecret` + `WithPlayerSecret(...)` copy
  method — the SDK auto-attaches `X-Player-Secret` to every
  request when set.
- Typed error helpers on `KratyApiError`:
  `IsInsufficientEntryCost`, `IsPlayerSecretInvalid`,
  `IsPlayerAlreadyRegistered`, `IsEntryRequirementFailed` (in
  addition to the existing `IsLobbyForming`).
- DTOs: `EntryCost`, `EntryCostCurrency`, `EntryCostItem`,
  `PlayerRegistration`, `PlayerItemHolding`, `PlayerWalletHolding`,
  `ConsumeItemInput` / `Result`, `DebitWalletInput` / `Result`,
  plus `Lobby.BotSlots` + `Lobby.FilledSlots` for smooth lobby
  fill UI.
- 24 new xUnit tests covering player auth, register flows,
  inventory/wallet, CollectAll happy path and partial failure,
  EntryCost decoding, and Lobby bot-slot projection (41 tests
  total).

### Changed

- `EventListing` gained `Type`, `LeaderboardMode`, `Metrics`,
  `EntryRequirement`, `EntryCost`, and `IsLobbyMatched` —
  bringing the wire shape to parity with the backend's listing
  response.
- `KratyErrorCode` expanded from 19 to 25 constants; missing
  codes (`player_secret_invalid`, `player_already_registered`,
  `insufficient_entry_cost`, `entry_requirement_failed`,
  `invalid_entry_requirement`, `max_daily_attempts_reached`) are
  now first-class.

## [0.0.1]

- Initial scaffold: `KratyClient` with retry / idempotency,
  `EventsClient`, `LeaderboardsClient` (read), `GrantsClient`
  (list/claim/open), `LobbiesClient`, `GrantPolling`,
  `LobbyPolling`.
