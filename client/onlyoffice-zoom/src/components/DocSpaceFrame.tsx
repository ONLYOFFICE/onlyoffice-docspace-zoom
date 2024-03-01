import { useEffect } from "react";

interface DocSpaceFrameProps {
    id: string;
    config: object;
}

function DocSpaceFrame({ id, config }: DocSpaceFrameProps) {
    useEffect(() => {
        const docSpaceInitConfig = Object.assign({
            checkCSP: false,
            frameId: id,
            name: id,
            theme: "System"
        }, config);

        console.debug("Opening DocSpace Frame", docSpaceInitConfig);
        const frame = window.DocSpace.SDK.initFrame(docSpaceInitConfig);

        return () => {
            if (frame) {
                window.DocSpace.SDK.frames[id].destroyFrame();
            }
        }
    }, [id, config])

    return (
        <div id={id}></div>
    );
}

export default DocSpaceFrame;
