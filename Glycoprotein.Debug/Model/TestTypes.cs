namespace Glycoprotein.Debug.Model;

record AddRequest(int A, int B);
record AddResponse(int Result);
record MultiplyRequest(int A, int B);
record MultiplyResponse(int Result);
record GreetRequest(string Name);
record GreetResponse(string Message);
record HeartbeatMessage(string NodeId, DateTime Time);
record AlarmMessage(string Level, string Description, DateTime Time);
