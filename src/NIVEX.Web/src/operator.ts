// Operator-supplied facts for the public About page.
// Leave a field empty until it is true and verified. Empty fields render as "not published yet";
// nothing here is ever invented by the site. See docs/launch-checklist-ko.md.
export interface OperatorInfo {
  operatorName: string;
  publicContact: string;
  supportEmail: string;
  // "registered" only when the business registration is actually completed.
  businessStatus: "" | "registered" | "not_registered";
  businessNumber: string;
  // "reported" only when the mail-order business report is actually filed.
  mailOrderStatus: "" | "reported" | "not_required" | "not_reported";
  mailOrderNumber: string;
  salesNotice: string;
}

export const operator: OperatorInfo = {
  operatorName: "",
  publicContact: "",
  supportEmail: "",
  businessStatus: "",
  businessNumber: "",
  mailOrderStatus: "",
  mailOrderNumber: "",
  salesNotice: "",
};
