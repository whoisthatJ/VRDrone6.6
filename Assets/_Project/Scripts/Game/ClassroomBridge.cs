using Ushqyr.Edu;
using UnityEngine;

namespace Ushqyr.Game
{
    /// <summary>
    /// Joins the class link to the game's scenes: when the teacher starts an activity on this seat, the place it is
    /// flown in is loaded and the activity begins there; when the teacher sends everyone back, the menu is loaded.
    /// </summary>
    public static class ClassroomBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Listen()
        {
            EduClassroom.StartRequested -= Start;
            EduClassroom.StartRequested += Start;
            EduClassroom.LeaveRequested -= Leave;
            EduClassroom.LeaveRequested += Leave;
        }

        private static void Start(ActivityDefinition activity, Assignment given, bool locked)
        {
            if (GameFlow.Catalog == null)
            {
                Debug.LogWarning("[Edu] the teacher started " + activity.Id + " before this seat knew its places; it was not started");
                return;
            }
            Time.timeScale = 1f;
            EduActivityRunner.LeaveBlocked = false;
            EduActivityRunner.SetPending(activity);
            EduActivityRunner.SetPendingAssignment(given);
            if (GameFlow.Activity(GameFlow.Catalog, activity.Setup.Place)) EduActivityRunner.LeaveBlocked = locked;
            else
            {
                EduActivityRunner.SetPending(null);
                EduActivityRunner.SetPendingAssignment(null);
            }
        }

        private static void Leave()
        {
            Time.timeScale = 1f;
            EduActivityRunner runner = EduActivityRunner.Current;
            if (runner != null) runner.Leave();
        }
    }
}
