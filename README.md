# ChineseCheckers
Focus on the AI algorithm implementation of Chinese Checkers, and use Unity to realize the game front-end.

## Story (2026.3)
I am an independent game developer who has continuously developed 5 games for Steam over the past few years. During the development of my latest puzzle game, my artist and I decided on a whim to add a small Chinese Checkers (hexagonal board) minigame to enhance the retro atmosphere reminiscent of the 1990s to the millennium era.

Although I completed the basic game mechanics in just a day or two, developing the Chinese Checkers AI (NPC) component ended up stumping me for two weeks. As a result, I decided to isolate this code independently to research how to implement the code with optimal performance and find the best possible solutions.

## Technical Info
- **Unity Version**: Unity 6000.2.9F1
- **Core AI Algorithms**: MinMax (V1/V2), Monte Carlo Tree Search (V3)
- **Board Type**: Hexagonal grid (Chinese Checkers standard)

## System Architecture
```mermaid
graph TD
    A[TQ_CheckerGameManager<br/>游戏核心管理器] --> B[ICheckerAIManager<br/>AI统一接口]
    B --> C[TQ_CheckerAIManagerV1<br/>MinMaxV1 AI]
    B --> D[TQ_CheckerAIManagerV2<br/>MinMaxV2 AI]
    B --> E[TQ_CheckerAIManagerV3<br/>MCTSV3 AI]
    
    E --> F[TQ_MCTSProcessorV3<br/>MCTS核心处理器]
    F --> G[TQ_MCTSNodeV3<br/>MCTS节点结构]
    E --> H[TQ_CheckerAIManagerEndgameV2<br/>A*残局逻辑]
    
    F --> I[TQ_RuleCore<br/>游戏规则核心]
    A --> J[TQ_HexBoardManager<br/>棋盘管理器]
    J --> K[TQ_HexBoardModel<br/>棋盘数据模型]
    
    L[AIVersion<br/>AI版本枚举] --> A
    M[TQ_AIDifficulty<br/>难度枚举] --> B