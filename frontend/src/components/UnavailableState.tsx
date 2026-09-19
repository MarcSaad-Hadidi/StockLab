import { useTranslation } from "react-i18next";
import "./unavailable-state.css";

export function UnavailableState({
  message = "businessData.unavailable",
}: {
  message?: string;
}) {
  const { t } = useTranslation();
  return (
    <div className="business-empty-state" role="status">
      <span aria-hidden="true">—</span>
      <strong>{t(message)}</strong>
      <p>{t("businessData.backendPending")}</p>
    </div>
  );
}
