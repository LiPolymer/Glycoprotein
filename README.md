# Glycoprotein

一个 ~~比较新奇的~~ IPC/微服务通讯库. 节点能够自动发现彼此, 注册可调用的函数/动作/事件, 并通过 RPC, 即发即弃分发以及发布/订阅消息进行通信, 所有内容均通过可插拔的传输层后端传输.

## 架构

```mermaid
flowchart TB
    subgraph GlycoComplex["GlycoComplex 节点"]
        direction TB
        RC[ResponseConductor<br/>接收 Query → 执行 Action/Function → 返回 Reply]
        QC[QueryConductor<br/>发送 Query → 等待 Reply]
        EE[EventEmitter<br/>发布 Event]
        ER[EventReceiver<br/>接收 Event → 匹配订阅]
        BP[BeaconPresenter<br/>周期性广播 Beacon]
        BT[BeaconTracker<br/>接收 Beacon → 发现/过期]
        subgraph Transport["传输层 (IConnexon 接口)"]
            direction LR
            T1[UnixDomainMeshConnexon<br/>全网状拓扑]
            T2[UnixDomainMasteredConnexon<br/>中心辐射 + 故障转移]
            T3[自定义实现<br/>TCP / WebSocket / ...]
        end
    end


    BP -- "Beacon ▶" --> Transport
    Transport -- "◀ Beacon" --> BT
    QC -- "Query ▶" --> Transport
    Transport -- "◀ Query" --> RC
    RC -- "Reply ▶" --> Transport
    Transport -- "◀ Reply" --> QC
    EE -- "Event ▶" --> Transport
    Transport -- "◀ Event" --> ER

    QC -.->|"查询活跃节点"| BT
    BP -.->|"读取已注册字段"| RC
    BP -.->|"读取已注册字段"| EE
```
具体用法参见 `Glycoprotein.Demo`

本项目基于 [`LGPLv3`](https://www.gnu.org/licenses/lgpl-3.0.zh-cn.html) 获得许可