# 变更记录（相对社区版 2.0.0.27）

## 2.1.0 — PRO 功能移植

### 元数据增强（19 项行为）
- TMDB/TVDB 中文优先、备选语言、屏蔽非回退语言简介
- 中文剧集名、原语言海报、拼音排序标题、优先繁体
- 剧集组：自动选组 / 重排季集号 / 本地 JSON
- 豆瓣：搜索补 ID、写简介与评分
- NFO 演员头像、演职人员增强（含人物刷新策略）
- 本地元数据缓存：写入（剧集 + 分集 + 海报）/ 还原（含海报）
- TMDb 响应缓存：TTL + 容量上限 + 命中统计 + 一键清空
- 增量处理：只处理自上次运行后变更的条目（可强制全量）

### 新增能力（2.1.0 期间补充）
- 全局代理（`GeneralOptions.GlobalProxy` + `ProxyUrl`）：Harmony 在 `ApplicationHost.CreateHttpClientHandler` 挂 postfix，把代理写进 Emby 新建的 SocketsHttpHandler，**所有出站请求**走代理；状态见 `GET /StrmAssistant/Proxy/Status`
- 抑制指定插件更新：过滤逻辑完成（单测 6 条）且挂钩已安装，但实测 postfix 不触发（详见 STATUS.md）
- 诊断端点：`GET /StrmAssistant/PluginUpdate/Probe`、`GET /StrmAssistant/Proxy/Status`、`GET /StrmAssistant/Dev/TypeProbe`
- **多版本美化**（`BeautifyMultiVersion` + 任务「多版本美化」）：按真实分辨率把版本命名为「名称 - 1080p」，可重复执行、演练不写库
- **播放进度按库分离**（`LibraryScopePlayProgress`）：给 `Video.GetUserDataKey` 追加库 id，同一内容在不同库各自记进度（默认关闭；开启会让现有进度归零一次）
- **阻止合集自动创建**（`NoBoxsetsAutoCreation`）：prefix 拦截 `CollectionManager.AddCollection`，选项开时不建 BoxSet
- **抑制指定插件更新**：挂钩验证执行（真实更新 + 名单 A/B 通过）

- 增量模式自动全量：检测到功能开关变化时自动按全量跑一次（并在日志说明）
- 演练模式全覆盖：豆瓣评分/简介、人物增强、剧集组映射、片头标记、演员头像写入都会跳过，且不推进增量进度
- 缓存对账端点 `GET /StrmAssistant/Cache/Verify`（orphans / missing，只读）
- 字幕优化：下载后自动转 UTF-8/简体（事件接线未验证）+ 手动接口 `POST /StrmAssistant/Subtitle/Optimize`
- 外部输入加固：NFO 演员名路径穿越、剧集组季集号越界、片头时间戳负值/超长、评分越界、图片 URL 协议与大小、配置导入路径下钻
- TMDb 缓存原子写入 + 半截文件识别
- 新增内容即时处理：订阅 `ILibraryManager.ItemAdded`，新分集入库后约 10 秒内抓片头标记（事件驱动，非轮询）
- 标记前集已看改为事件驱动：订阅 `IUserDataManager.UserDataSaved`，播放进度达阈值立刻生效
- 演练模式 `DryRun`：只记录将要做的事，不写库不写文件
- 剧集组扩展信息：应用剧集组时连同组内分集名一起写入
- 豆瓣辅助范围：全部 / 仅电影 / 仅剧集
- 豆瓣 Cookie / AccessToken / TidbApiKey 真正参与请求
- 本地元数据缓存：新增「清理孤儿目录」任务与仪表盘「清空」按钮
- 配置导出 / 导入（扁平键值对，约 84 项）
- 高级选项折叠开关 `ShowMetadataExtraOptions` 生效

### 片头片尾
- 外部片头库匹配并写入章节标记（IntroDb / Tidb）
- 每日 04:00 自动补全（可配置为每 6 小时轮询）

### 体验增强
- 深度删除（条目 + strm + 目标文件）
- 强制媒体库顺序
- UI 功能：隐藏无头像/非中文人物、仅演员、剧集季倒序、首页库条目数、未看→总集数
- 标记前集已看（播放进度达阈值后，按 2 分钟轮询）

### 运维
- 依赖自检、冲突插件检测、内存看门狗、刮削代理、配置导出/导入
- 任务完成可写入活动日志
- 五个任务：元数据增强 / 片头标记补全 / 豆瓣评分更新 / 从缓存还原 / 标记前集已看

## 已实现但曾长期失效的选项（本轮已修复）
| 选项 | 之前 |
|---|---|
| `EnableImageCapture` | 属性写死 `=> false` |
| `EnableAutomaticIntroScraping` | 触发器读不到配置，永远不触发 |
| `DoubanCookie` / `DoubanAccessToken` | 代码从未读取 |
| `TidbApiKey` | 所有提供方共用公共 key |

## 未实现（附原因）
| 项 | 原因 |
|---|---|
| 播放行为类 UI 功能（进度分离等） | 需要拦截 Emby 内部调用，本环境挂钩不可用 |
| 全局代理 / 本地发现 URL | 同上；本插件只代理自己的刮削请求 |
| 扫描时同步刮削 | 同上；改为定时轮询（延迟 ≤ 轮询周期） |
| ~~多版本美化~~ | ✅ 已实现并验证（见 `docs/交付报告.md`） |
| MDBList（烂番茄评分） | 需要外部 API 契约，未凭空实现 |
| 日志页倒序 | 未能定位 Emby 日志页 DOM，按规矩不发布未验证开关 |
| `DoubanAssistScrapeScope` | 豆瓣辅助范围过滤未实现 |
| `EnableMovieDbEpisodeGroupExtendedInfo` | 剧集组扩展信息未实现 |
| `EnhanceMissingEpisodes` | 缺集补全未实现 |
| `EnhanceNotification` | 调用链已通（`SendNotification` 已调用并记日志），但投递无法在本环境验证 |
| `FolderVideoGroupingLimit` | 穷举 Emby 配置属性，只有 `EnableAutomaticSeriesGrouping`，无"分组上限" |
| `NoBoxsetsAutoCreation` | `ILibraryManager` 未暴露修改库选项的方法（只有 Get/Add） |
| `OptimizeSubtitle` | 变换与手动接口已验证；下载后自动触发未验证（缺字幕提供方） |
| `ShowMetadataExtraOptions` | UI 折叠开关未实现 |
| `SuppressPluginUpdates`（抑制指定插件更新） | 字段是"插件名列表"，需在更新检查里按名过滤，只能靠挂钩 |
