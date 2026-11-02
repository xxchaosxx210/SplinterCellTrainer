#pragma once
// ULevel::SpawnActor takes Template before the flags and Owner after them.
namespace InventorySpawn {
template<class Spawn,class Vec,class Rot>
auto Create(Spawn spawn,void* level,void* itemClass,Vec position,Rot rotation,void* controller){
    return spawn(level,itemClass,0,position,rotation,nullptr,0,0,controller,nullptr);
}
}
