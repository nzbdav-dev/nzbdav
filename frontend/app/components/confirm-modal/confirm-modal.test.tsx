import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
import { ConfirmModal } from "./confirm-modal";

describe("ConfirmModal", () => {
    it("disables confirmation until a required checkbox is selected", () => {
        const html = renderToStaticMarkup(
            <ConfirmModal
                show
                title="Confirm"
                message="Review the warning"
                checkboxMessage="I understand"
                requireCheckbox
                onCancel={vi.fn()}
                onConfirm={vi.fn()}
            />,
        );

        expect(html).toContain("disabled=\"\"");
    });

    it("keeps confirmation enabled when acknowledgement is optional", () => {
        const html = renderToStaticMarkup(
            <ConfirmModal
                show
                title="Confirm"
                message="Review the warning"
                onCancel={vi.fn()}
                onConfirm={vi.fn()}
            />,
        );

        expect(html).not.toContain("disabled=\"\"");
    });
});
