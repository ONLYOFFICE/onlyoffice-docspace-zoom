import { ConfigResponse } from "@zoom/appssdk";
import Payload from "./Payload";
import MeetingState from "./MeetingState";

class AppState {
    payload: Payload | null = null;
    zoomConfig: ConfigResponse | null = null;
    meetingState: MeetingState = new MeetingState();
}

export default AppState;