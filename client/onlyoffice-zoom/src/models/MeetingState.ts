import AdditionalMeetingState from "./AdditionalMeetingState";

class MeetingState {
    currentMeetingId: string | null = null;
    currentCollaborationId: string | null = null;
    isHost: boolean = false;

    isFirstPick: boolean = true;
    currentFileId: number | null = null;
    additionalInfo: AdditionalMeetingState = new AdditionalMeetingState();
}

export default MeetingState;