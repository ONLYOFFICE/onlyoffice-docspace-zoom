import { useCallback } from "react";
import "./Confirm.css";
import DocSpaceFrame from "./DocSpaceFrame";

interface ConfirmProps {
    url: string;
    onAuthCallback: (success: boolean) => void;
}

function Confirm({ url, onAuthCallback }: ConfirmProps) {

    const onAuthSuccess = useCallback(() => {
        console.log("DocSpace Authentication success");
        onAuthCallback(true);
    }, [onAuthCallback]);

    const onAuthError = useCallback((error: any) => {
        console.log("DocSpace Authentication fail", error);
        onAuthCallback(false);
    }, [onAuthCallback]);

    const sanitized = new URL(url);
    const config = {
        src: sanitized.origin,
        rootPath: sanitized.href.substring(sanitized.origin.length),
        mode: "custom",
        events: {
            onAuthSuccess: onAuthSuccess,
            onAppError: onAuthError
        }
    };

    console.debug("Initing DocSpace with ConfirmLink");

    return (
        <div className="confirm">
            <DocSpaceFrame id="zoom-ds-auth-frame" config={config} />
        </div>
    );
}

export default Confirm;