
## 重要：前端脚本注入的权限要求

插件的前端增强（隐藏人物、倒序、库条目数、未看→总数）依赖把一行脚本注入
Emby 的 `/system/dashboard-ui/index.html`。

Emby 容器里服务进程通常以非 root 用户（如 `bin`）运行，而该文件属于 `root`，
默认不可写。此时日志会出现：

```
ScriptInject - failed: Access to the path '/system/dashboard-ui/index.html' is denied.
```

处理办法（容器部署）：

```bash
docker exec -u root <容器名> chmod 666 /system/dashboard-ui/index.html
docker restart <容器名>
```

或重建容器时挂载可写副本。成功后日志会显示：

```
ScriptInject - patched /system/dashboard-ui/index.html
```

插件本身也会在启动后每 20 秒重试一次，最多 6 次（约 2 分钟）——如果权限在这期间被
调整好，无需再重启。
