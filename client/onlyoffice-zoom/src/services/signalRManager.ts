import zoomSdk from "@zoom/appssdk";
import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

let connection: HubConnection | null = null;

async function initSignalR(meetingUUID: string) {
    console.log("Initing SignalR");
    if (connection) {
        console.log("SignalR already inited");
    }

    const appContext = await zoomSdk.getAppContext();

    connection = new HubConnectionBuilder()
        .withUrl(`/zoomservice/hubs/zoom?meetingId=${encodeURIComponent(meetingUUID)}&zoomAppContextToken=${appContext.context}`)
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Information)
        .build();

    try {
        await connection.start();
    } catch (e) {
        console.log("SignalR error", e);
        throw e;
    }
}

function invoke<T>(methodName: string, ...args: any[]): Promise<T> {
    if (connection != null) {
        return connection.invoke(methodName, ...args);
    } else {
        return new Promise<T>((_, rej) => rej("SignalR is not ready"));
    }
}

export {
    initSignalR,
    invoke
};