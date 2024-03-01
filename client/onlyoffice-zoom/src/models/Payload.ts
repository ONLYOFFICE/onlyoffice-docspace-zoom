import CollaborationPayload from "./CollaborationPayload";

class Payload {
    homeTenant: string | null = null;
    docSpaceUrl: string | null = null;

    confirmLink: string | null = null;
    collaboration: CollaborationPayload | null = null;

    state: string | null = null;
    challenge: string | null = null;
}

export default Payload;