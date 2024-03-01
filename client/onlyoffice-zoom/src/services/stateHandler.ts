import AppState from "../models/AppState";

const currentState = new AppState();

function getState() {
    return currentState;
}

export default getState;