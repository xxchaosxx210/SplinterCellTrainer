#pragma once
#include <cmath>
namespace FightPolicy {
inline bool MaintainSession(unsigned long long now,unsigned long long deadline,bool samePlayer,bool sameLevel) {
    return now<deadline&&samePlayer&&sameLevel;
}
struct Candidate { unsigned id; float x,y,z; };
inline bool Eligible(bool guard,bool hostile,bool sameLevel,bool armed,bool conscious,bool controlled,bool deleted,int health) {
    return guard&&hostile&&sameLevel&&armed&&conscious&&controlled&&!deleted&&health>0;
}
inline int Nearest(const Candidate* actors,int count,int self) {
    if(!actors||self<0||self>=count)return -1;
    int best=-1;float distance=2000.0f*2000.0f;
    for(int i=0;i<count;++i){
        if(i==self||actors[i].id==actors[self].id)continue;
        float x=actors[i].x-actors[self].x,y=actors[i].y-actors[self].y,z=actors[i].z-actors[self].z;
        float d=x*x+y*y+z*z;
        if(std::isfinite(d)&&d<=distance&&(best<0||d<distance)){best=i;distance=d;}
    }
    return best;
}
}
