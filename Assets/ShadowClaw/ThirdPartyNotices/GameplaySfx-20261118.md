# Gameplay SFX 现成素材与授权记录（2026-11-18）

本次仅导入 10 个短音频到 `Assets/ShadowClaw/Resources/GameplaySfx/`，供玩法端接入。全部来自免费 CC0 素材；没有生成合成音、购买、登录或联系作者。原包和未选候选保留在 `D:/Shadow/Downloads/Audio-20261118/`，未把完整素材包放进 Resources。

## 已采用来源

| 用途 | 原作者与官方作品页 | 原始文件 | 真实性与限制 |
| --- | --- | --- | --- |
| 弩发射 | Ben Jaszczak & Brian Nelson；[Medieval sound effects - Weapon Textures](https://opengameart.org/content/medieval-sound-effects-weapon-textures) | `Crossbow Shoot.wav` | 页面明确说明由两位作者制作并录制；截取同一连续录音的前三次独立弩发射，没有用长弓或激光代替。 |
| 箭撞墙地 | Kenney；[Impact Sounds](https://kenney.nl/assets/impact-sounds) | `impactSoft_heavy_000..002.ogg` | 现成 Foley 软撞击声，用于闷击候选；不能称为“真实箭射入土壤”录音。当前未区分石、木、土材料，后续可在同包挑对应材料。 |
| 飞刀投掷 | artisticdude；[Swishes Sound Pack](https://opengameart.org/content/swishes-sound-pack) | `swish-1.wav`、`swish-3.wav` | 作者在原页讨论用实物挥动录制风切；这是挥动声，不声称录的是投出去的刀。独立于弩声。 |
| 点灯燃烧 | AntumDeluge；[Fire Crackling](https://opengameart.org/content/fire-crackling) | `fire-1.wav` | 作者描述为壁炉的爆裂、噼啪声；截成两个短燃烧片段。页面未交代录音设备或处理链，因此不宣称未经处理的原始实录。不是持续循环，也没有额外合成法术音头。 |

以上页面均标注 [Creative Commons Zero 1.0（CC0）](https://creativecommons.org/publicdomain/zero/1.0/)。Kenney 压缩包内 `License.txt` 同时确认可用于个人及商业项目，署名非强制。为保留来源和方便维护，本文件仍列出作者。OGA 页面 HTML 快照已与原包一起保存在 Downloads；这些是来源快照，不是项目运行资源。

原始下载：

- [中世纪武器原包第 1 部分](https://opengameart.org/sites/default/files/medieval_sfx_textures_1_of_2.7z)
- [Swishes 原包](https://opengameart.org/sites/default/files/swishes.zip)
- [Fire Crackling 原始 WAV](https://opengameart.org/sites/default/files/fire-1.wav)
- Kenney 使用作品页内的当前 `download-link` 接口取得带有效期的官方下载链接。旧 `kenney_rpg-audio.zip`、`kenney_impact-sounds.zip` 实际为 HTML 页面，保留原文件但没有当作音频或压缩包使用；有效包另存为 `kenney-rpg-audio-current.zip`、`kenney-impact-sounds-current.zip`。

## 导入清单与处理

统一为 **48 kHz、单声道、PCM16 WAV**。仅作原录音截取、声道合并、采样率转换、短淡入淡出和固定增益；未改变播放速度、音高或叠加新声音。Unity importer 设置短音频预载、PCM、关闭额外 normalize，避免 Unity 二次归一化改变已经记录的峰值。

| Resources 名称（不含扩展名） | 时长（秒） | 来源片段（秒） | 淡入 / 淡出（毫秒） | 处理增益（dB） |
| --- | ---: | --- | --- | ---: |
| `GameplaySfx/Crossbow_01` | 0.228000 | 0.322000–0.550000 | 3 / 18 | -1.806 |
| `GameplaySfx/Crossbow_02` | 0.201000 | 0.849000–1.050000 | 3 / 18 | +0.218 |
| `GameplaySfx/Crossbow_03` | 0.257000 | 1.393000–1.650000 | 3 / 18 | -0.060 |
| `GameplaySfx/ArrowImpact_01` | 0.505104 | 0.000000–0.505102 | 1 / 12 | -2.829 |
| `GameplaySfx/ArrowImpact_02` | 0.572042 | 0.000000–0.572041 | 1 / 12 | -2.777 |
| `GameplaySfx/ArrowImpact_03` | 0.572333 | 0.000000–0.572313 | 1 / 12 | -2.862 |
| `GameplaySfx/KnifeThrow_01` | 0.126042 | 0.000000–0.126032 | 2 / 8 | -3.462 |
| `GameplaySfx/KnifeThrow_02` | 0.145854 | 0.000000–0.145850 | 2 / 8 | -3.446 |
| `GameplaySfx/LampIgnite_01` | 1.600000 | 0.000000–1.600000 | 12 / 120 | +4.931 |
| `GameplaySfx/LampIgnite_02` | 1.600000 | 1.200000–2.800000 | 12 / 120 | +5.050 |

总 WAV 大小 **557,948 字节**（不含 meta）。三个弩声是原始连录中的不同发射；两段燃烧来自同一 2.870612 秒原文件，片段有部分重叠，并不冒充两次独立录制。

验证范围：用 SoundFile 解码重新检查全部 10 个输出的长度、声道、采样率、有限样本、峰值低于满幅、首末样本接近零。只做了文件和波形层面的检查，**执行代理不能实际听见音频，尚未完成听感验收**；更不能据此宣称在游戏混音中一定自然、音量合适。Unity 导入、触发、空间衰减与实际耳机试听由统一集成验收负责。本资源任务未启动 Unity、未运行构建、未修改运行代码或场景。

## 后续待补来源（未导入、未承诺听感）

| 后续用途 | 可继续筛选的免费来源 | 当前状态 |
| --- | --- | --- |
| 弩装填、扳机、箭经过身旁 | 上述 Medieval Weapon Textures 包中的 Crossbow Lever Trigger、Place Bolt、Bolt Pass/Passby | 已保留原包；未导入这些条目。 |
| 木、金属等不同命中材质与脚步 | [Kenney Impact Sounds](https://kenney.nl/assets/impact-sounds) | 全包已解压到 Downloads；只有本次三个软撞击进入 GameplaySfx。 |
| 门、布料、皮革、拔刀等交互 | [Kenney RPG Audio](https://kenney.nl/assets/rpg-audio) | 已核验 CC0 包内许可证；原包已下载。未当作弩发射源。 |
| 更强的点燃瞬态或 Boss 火焰攻击 | LEGIT Audio；[Fire Staff Sound Effects](https://opengameart.org/content/fire-staff-sound-effects) | CC0，25 条 WAV 已在 Downloads。1–21 条各 8.5 秒，22–25 条各 17 秒，多数有较长尾部静音。作者描述为火杖音效；未证明全部是未经处理的火焰实录，本轮未导入。 |
| 灯具持续燃烧层 | PagDev；[Fireplace Sound loop](https://opengameart.org/content/fireplace-sound-loop) | 来源页标注 CC0、木柴壁炉循环；尚未下载和试听，不在本次点灯短声中。 |
| 怪物发现、受击、死亡 | Ogrebane；[Monster Sound Pack, Volume 1](https://opengameart.org/content/monster-sound-pack-volume-1) 与 [Monster Sound Effects Pack](https://opengameart.org/content/monster-sound-effects-pack) | 来源页标注 CC0，含 growl/grunt/death 等；尚未下载和试听，仍需与侍僧、地影、Boss 个性匹配。 |

## SHA-256 与可复现记录

处理参数、原始文件与输出的散列还保存于 `D:/Shadow/Downloads/Audio-20261118/selected-gameplay-sfx.json`；处理脚本是同目录的 `prepare-gameplay-sfx.py`。脚本拒绝覆盖已存在的输出，以免误改接入中的资源。

原始包 / 文件：

| 文件（相对于 Downloads/Audio-20261118） | SHA-256 |
| --- | --- |
| `medieval_sfx_textures_1_of_2.7z` | `b5e2b2c86d811d983d15dbf8453df6a25275a2a0dc4e88e0dc5e1cb3046fd4ae` |
| `kenney-impact-sounds-current.zip` | `029d734af1582474edf3a694d1b0cebc97c1c152f2f39fa34d4c2bafc5de77f8` |
| `kenney-rpg-audio-current.zip` | `6dbeaf8544da958d8f2adcb4a4a4b76c1ade34a05f8ab9edccd327da7375f38b` |
| `swishes.zip` | `7980215241b739a787dcf26f660ce510bb237900968789395a86911619795693` |
| `fire-1.wav` | `26702de8a195bcbafeae72034861d875a1b7917168a25106f4e1957ecdddf7e5` |
| `legit-fire-staff.zip` | `8c0668fdde03de52b9b201e672244992ae739438937d4bef6908d1618a86b020` |

已使用原始音频：

| 文件 | SHA-256 |
| --- | --- |
| `MedievalWeapons/Crossbow Shoot.wav` | `d8c68f970540d73ab6fe2db5309166e6dd911876501a16bb4df43498247e19b2` |
| `KenneyImpact/Audio/impactSoft_heavy_000.ogg` | `49e7ca88743fca974bb8676ea138b751cfd8f9033b5e7af8736c2a215d6edbc1` |
| `KenneyImpact/Audio/impactSoft_heavy_001.ogg` | `4d0096364ba9e46119d2ff6df493fdc101bd2e1efae061da5e5f77a53b3fdcb6` |
| `KenneyImpact/Audio/impactSoft_heavy_002.ogg` | `d130ae541951243d3f6ed963d0057450a445ffe117fbf9a084d34eb1635cb563` |
| `Swishes/swishes/swish-1.wav` | `9a15c881345c1ce1c39cc31f52f081c8c4dc81fa41111e0079400f39d3ba0e45` |
| `Swishes/swishes/swish-3.wav` | `55b100e2c67fa43dad8510e31e85bbd51a68d5ce84a6e0259e21e19030083e24` |
| `fire-1.wav` | `26702de8a195bcbafeae72034861d875a1b7917168a25106f4e1957ecdddf7e5` |

最终导入音频：

| 文件 | SHA-256 |
| --- | --- |
| `Crossbow_01.wav` | `99c5323292577238c554ad1e5e266c18a42e16177e71d5a88bae9fc664df2190` |
| `Crossbow_02.wav` | `53bea271f52a3aefebd582c152d29df39053142945b903f92727b0c85e2d7aff` |
| `Crossbow_03.wav` | `0ac068acc02d5c51e8cf495007851a569838a47d11f18da70909f6c35a7fa6c0` |
| `ArrowImpact_01.wav` | `40668650db2fdad11125381c5724edd493a407b1c41ca3e38c2754e3c45823ea` |
| `ArrowImpact_02.wav` | `a45d740a56517f679b6e7ace37fc65f9e4abc279241d4513532b67ab681f388a` |
| `ArrowImpact_03.wav` | `684dbba696bdaea4645d0c436b84268caa9b98cb68be7ccb1c5ac2c41000bbe2` |
| `KnifeThrow_01.wav` | `be43596e3aac94814b51c8c9681ab0776fb5b6eeab143a0685c8b4e6291fd505` |
| `KnifeThrow_02.wav` | `04f2fee312029a3eefbe6bc784993744358095de93f029be69a133baadfbc7a1` |
| `LampIgnite_01.wav` | `6d49efc62b79738042b28f5637a481b517092277f8e7910bc10c4c7d5889585b` |
| `LampIgnite_02.wav` | `217199820be232b71f0441c809a2e4f184efea861e2b76209c69a00746b0efa2` |

补充（2026-11-19）：猎魔人受伤使用 Kenney《Impact Sounds》中的 `impactPunch_medium_002.ogg`，原音频以 `HunterHurt.ogg` 收录，授权为 CC0 1.0。
