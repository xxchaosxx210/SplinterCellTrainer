#pragma once
#include <cmath>
namespace LockPolicy {
constexpr float RadiusSquared=250.0f*250.0f;
inline bool PrepareDoor(bool usable,bool locked,bool closed,bool specialOpener,float distanceSquared,float radiusSquared=RadiusSquared) {
    return usable&&locked&&closed&&!specialOpener&&std::isfinite(distanceSquared)&&distanceSquared>=0&&distanceSquared<=radiusSquared;
}
inline bool RestoreDoor(bool closed,bool interactionCommitted){return closed&&!interactionCommitted;}
inline bool GrantKeypad(bool playerInKeypad,bool ownedByPlayer,bool keypadInUse){return playerInKeypad&&ownedByPlayer&&keypadInUse;}
}
