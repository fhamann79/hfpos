import { Validators } from '@angular/forms';

export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 256;
export const NEW_PASSWORD_VALIDATORS = [Validators.required,
  Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH),
  Validators.pattern(/\S/)];
