#pragma once
namespace NpcTrackingPolicy {
inline bool CanBind(bool validController,bool sameLevel,bool deleting,unsigned linkedPawn,unsigned trackedPawn) {
    return validController&&sameLevel&&!deleting&&trackedPawn!=0&&linkedPawn==trackedPawn;
}
}
