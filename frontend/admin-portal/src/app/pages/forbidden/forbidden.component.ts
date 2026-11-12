import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="page" style="place-items:center;min-height:60vh">
      <div class="card" style="max-width:520px;text-align:center">
        <div class="card-body">
          <div style="font-size:44px">🔒</div>
          <h1 style="margin-top:10px">Access restricted</h1>
          <p class="muted" style="margin-top:8px">
            Your role does not include permission for this page. Ask your school administrator if you need access.
          </p>
          <a class="btn btn-primary" routerLink="/dashboard" style="margin-top:18px">Back to dashboard</a>
        </div>
      </div>
    </div>
  `
})
export class ForbiddenComponent {}
