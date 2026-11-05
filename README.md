# 神医助手 Lite（StrmAssistant-WM）

Emby 插件，面向 strm 媒体库：媒体信息提取与持久化、中文元数据增强、片头片尾、多版本合并、拼音 / 模糊搜索、Strm 直连等。

基于 [sjtuross/StrmAssistant](https://github.com/sjtuross/StrmAssistant) 移植和改写，沿用 GPL-3.0 协议。

## 安装

1. 到 [Releases](https://github.com/CodeApe-Xiaoyin/StrmAssistant-WM/releases) 下载**与你的 Emby 版本对应**的包：

   | Emby 服务端 | 安装包 |
   |---|---|
   | 4.10.x | `StrmAssistantLite-emby4.10.zip` |
   | 4.9.x | `StrmAssistantLite-emby4.9.zip` |
   | 4.8.x | `StrmAssistantLite-emby4.8.zip` |

   版本不能混用，装错了插件加载不了。Emby 版本在「设置 → 仪表盘」右上角。

2. 解压到 Emby 的插件目录（Docker 一般是 `/config/plugins/`），保持目录结构：

   ```
   plugins/
   ├── StrmAssistantLite.dll
   ├── StrmAssistantDeps/   （0Harmony、TinyPinyin、ChineseConverter，必须保持这个目录名）
   ├── zh/
   └── zh-hant/
   ```

3. 重启 Emby，进入「设置 → 插件 → 神医助手」。

## 更新

插件首页「计划任务 → 更新插件」点「执行」，会从本仓库最新 Release 下载对应版本的包并覆盖，重启 Emby 后生效。默认每周自动检查一次。
GitHub 访问慢时，在「更新插件 → 配置」里填下载加速前缀，或在「常规设置 → 网络代理」里填代理。

## 页面

- **神医助手（首页）**：运行状态（刷新队列进度、内存）、常用计划任务（「配置」「执行」）、维护工具。
- **常规设置**：后台任务并发、追更模式、网络代理（协议按地址前缀自动识别）。
- **媒体信息提取**：媒体库范围、JSON 持久化、播放时预加载下一集、打开详情页时提取、截图。
- **元数据增强**：TMDB 中文优化、剧集组、豆瓣、替代 TMDB 地址、本地元数据缓存。
- **片头片尾增强**：Emby 片头检测支持 strm、播放时学习片头片尾、外部片头库。
- **体验增强**：多版本合并与美化、中文搜索增强、Emby 直连（302）、深度删除、通知 Webhook、界面功能。

## 可选：前端脚本注入

「体验增强 → 界面」里的部分功能需要往 Emby 网页注入脚本，前提是 Emby 进程能写 `index.html`：

```bash
chmod 666 /system/dashboard-ui/index.html
```

## 构建

需要 .NET 8 SDK（4.8 包另需 .NET 6 目标包，SDK 会自动还原）。

1. 构建需要 Emby 自带的 3 个程序集，本仓库不分发，从你自己的 Emby 安装里取：

   ```bash
   ./scripts/fetch-libs.sh <Emby 根目录> 4.10   # 4.9 / 4.8 同理
   ```

2. 构建并打包：

   ```bash
   ./scripts/build-emby.sh all      # 产物在 dist/plugins、dist/emby4.9/plugins、dist/emby4.8/plugins
   ```

3. 单元测试：

   ```bash
   dotnet run --project tests/LogicTests/LogicTests.csproj
   ```

## 协议

GPL-3.0，见 [LICENSE](LICENSE)。
