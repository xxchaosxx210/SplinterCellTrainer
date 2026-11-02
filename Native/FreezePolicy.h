#pragma once
namespace FreezePolicy {
inline unsigned RestoreBits(unsigned current,unsigned original,unsigned mask){return (current&~mask)|(original&mask);}
inline bool Expired(unsigned long long now,unsigned long long heartbeat){return now-heartbeat>15000;}
}
