# Published once per control step, after that step's sensor messages.
builtin_interfaces/Time stamp   # simulator time since the start signal
uint32 tick                     # control steps since the start signal
bool running
string status                   # waiting, running, then why it ended: finished, time_limit, stuck, disconnected
