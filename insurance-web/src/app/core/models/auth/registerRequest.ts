// Mirrors Api/DTOs/Auth/RegisterRequest.cs. The API joins first and last name, and the address parts, itself.
export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  phone?: string;
  street: string;
  unit?: string;
  city: string;
  state: string;        // two-letter code
  postalCode: string;
  paymentReminders?: boolean;   // "Email me payment reminders" (left out = yes)
}
