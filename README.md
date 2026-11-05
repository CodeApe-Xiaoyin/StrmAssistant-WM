# 神医助手 Lite（StrmAssistant-WM）

Emby 插件，面向 strm 媒体库：媒体信息提取与持久化、中文元数据增强、片头片尾、多版本合并、拼音 / 模糊搜索、Strm 直连等。

基于 [sjtuross/StrmAssistant](https://github.com/sjtuross/StrmAssistant) 移植和改写，沿用 GPL-3.0 协议。

## 安装

1. 到 [Releases](https://github.com/CodeApe-Xiaoyin/StrmAssistant-WM/releases) 下载 `StrmAssistantLite.zip`。
   同一个包支持 Emby 4.8 / 4.9 / 4.10，不用再按版本挑。

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

插件首页「计划任务 → 更新插件」点「执行」，会从本仓库最新 Release 下载安装包并覆盖，重启 Emby 后生效。默认每周自动检查一次。
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

需要 .NET 8 SDK。

插件按 Emby 4.8 的接口、.NET 6 编译，所以 4.8 / 4.9 / 4.10 都能加载（新版 Emby 能加载引用旧接口的插件，反过来不行）。
三个版本不一样的接口在 `StrmAssistant/Common/EmbyCompat.cs` 里运行时处理。

1. 构建需要 Emby 4.8 自带的 3 个程序集，本仓库不分发，从 4.8 的 Emby 里取：

   ```bash
   ./scripts/fetch-libs.sh <Emby 4.8 根目录> 4.8
   ```

2. 构建并打包：

   ```bash
   ./scripts/build-emby.sh          # 产物：dist/plugins/ 和 dist/StrmAssistantLite.zip
   ```

3. 核对接口兼容（发布前）：DLL 引用的每个 Emby 接口在各版本服务端里都要存在、签名一致：

   ```bash
   dotnet run --project tools/ApiCheck -- StrmAssistant/bin/Release/net6.0/StrmAssistantLite.dll <Emby 4.10 的 system 目录>
   ```

   4.8 / 4.9 / 4.10 各跑一次，都应输出「问题 0 个」。

4. 单元测试：

   ```bash
   dotnet run --project tests/LogicTests/LogicTests.csproj
   ```

## 协议

GPL-3.0，见 [LICENSE](LICENSE)。
