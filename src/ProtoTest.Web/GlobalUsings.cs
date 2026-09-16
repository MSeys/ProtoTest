#if NET9_0_OR_GREATER
global using ProtoLock = System.Threading.Lock;
#else
global using ProtoLock = System.Object;
#endif
