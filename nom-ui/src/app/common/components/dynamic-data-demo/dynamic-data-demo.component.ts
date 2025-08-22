import { Component } from '@angular/core';
import { FormControl } from '@angular/forms';

@Component({
    selector: 'app-dynamic-data-demo',
    template: `
    <div class="dynamic-data-demo">
      <div class="demo-header">
        <h1>Dynamic Data Components Demo</h1>
        <p class="subtitle">Showcasing the replacement of hardcoded UI data with dynamic backend data</p>
      </div>

      <div class="demo-content">
        <!-- Shopping Components -->
        <section class="demo-section">
          <h2>Shopping Module Components</h2>
          <div class="component-grid">
            <div class="component-card">
              <h3>Shopping List Component</h3>
              <p>Dynamic filtering and display of shopping items with backend-driven priorities and categories</p>
              <div class="component-preview">
                <app-shopping-list></app-shopping-list>
              </div>
            </div>

            <div class="component-card">
              <h3>Shopping Item Form</h3>
              <p>Form for adding shopping items using dynamic reference data for priorities and categories</p>
              <div class="component-preview">
                <app-shopping-item-form></app-shopping-item-form>
              </div>
            </div>
          </div>
        </section>

        <!-- Recipe Components -->
        <section class="demo-section">
          <h2>Recipe Module Components</h2>
          <div class="component-grid">
            <div class="component-card">
              <h3>Recipe Form Component</h3>
              <p>Comprehensive recipe creation form using dynamic data for difficulty levels, cuisine types, and meal types</p>
              <div class="component-preview">
                <app-recipe-form></app-recipe-form>
              </div>
            </div>
          </div>
        </section>

        <!-- Meal Planning Components -->
        <section class="demo-section">
          <h2>Meal Planning Module Components</h2>
          <div class="component-grid">
            <div class="component-card">
              <h3>Meal Plan Form Component</h3>
              <p>Meal planning form with dynamic day selection and meal type assignment</p>
              <div class="component-preview">
                <app-meal-plan-form></app-meal-plan-form>
              </div>
            </div>
          </div>
        </section>

        <!-- Reference Selector Component -->
        <section class="demo-section">
          <h2>Generic Reference Components</h2>
          <div class="component-grid">
            <div class="component-card">
              <h3>Reference Selector Component</h3>
              <p>Reusable component for selecting any type of reference data from the backend</p>
              <div class="component-preview">
                <h4>Example: Shopping Priority Selection</h4>
                <app-reference-selector
                  [discriminatorId]="6000"
                  [control]="demoControl"
                  label="Select Priority"
                  placeholder="Choose a priority level"
                  [showDescription]="true">
                </app-reference-selector>
              </div>
            </div>
          </div>
        </section>

        <!-- Implementation Details -->
        <section class="demo-section">
          <h2>Implementation Details</h2>
          <div class="details-grid">
            <div class="detail-card">
              <h3>Backend Architecture</h3>
              <ul>
                <li>✅ Extended ReferenceDiscriminatorEnum with new UI data groups (6000-6016 series)</li>
                <li>✅ Created new View Entities for each reference type</li>
                <li>✅ Updated ApplicationDbContext with new discriminators</li>
                <li>✅ Extended ReferenceController with new endpoints</li>
                <li>✅ Implemented ReferenceOrchestrationService</li>
                <li>✅ Seeded all new reference data in _CustomMigration.cs</li>
              </ul>
            </div>

            <div class="detail-card">
              <h3>Frontend Services</h3>
              <ul>
                <li>✅ Generic ReferenceDataService for all reference data</li>
                <li>✅ Specialized services for Shopping, Recipe, and Meal Planning</li>
                <li>✅ Bulk loading capabilities for performance</li>
                <li>✅ Intelligent caching system</li>
                <li>✅ Type-safe reference ID constants</li>
              </ul>
            </div>

            <div class="detail-card">
              <h3>Component Architecture</h3>
              <ul>
                <li>✅ Reusable ReferenceSelectorComponent</li>
                <li>✅ Shopping components with dynamic data</li>
                <li>✅ Recipe form with dynamic classifications</li>
                <li>✅ Meal planning with dynamic scheduling</li>
                <li>✅ Responsive design and modern UI</li>
                <li>✅ Form validation and error handling</li>
              </ul>
            </div>

            <div class="detail-card">
              <h3>Data Flow</h3>
              <ul>
                <li>✅ Components request data via specialized services</li>
                <li>✅ Services fetch from ReferenceController API endpoints</li>
                <li>✅ Data is cached for performance</li>
                <li>✅ UI updates dynamically based on backend data</li>
                <li>✅ No more hardcoded strings or magic numbers</li>
                <li>✅ Centralized data management</li>
              </ul>
            </div>
          </div>
        </section>

        <!-- Benefits -->
        <section class="demo-section">
          <h2>Benefits of Dynamic Data Implementation</h2>
          <div class="benefits-grid">
            <div class="benefit-item">
              <mat-icon>sync</mat-icon>
              <h4>Real-time Updates</h4>
              <p>UI automatically reflects changes made to reference data in the backend</p>
            </div>
            <div class="benefit-item">
              <mat-icon>storage</mat-icon>
              <h4>Centralized Management</h4>
              <p>All reference data managed in one place through the Reference system</p>
            </div>
            <div class="benefit-item">
              <mat-icon>speed</mat-icon>
              <h4>Performance</h4>
              <p>Bulk loading and intelligent caching for optimal performance</p>
            </div>
            <div class="benefit-item">
              <mat-icon>code</mat-icon>
              <h4>Maintainability</h4>
              <p>No more scattered hardcoded values throughout the codebase</p>
            </div>
            <div class="benefit-item">
              <mat-icon>expand_more</mat-icon>
              <h4>Scalability</h4>
              <p>Easy to add new reference types without code changes</p>
            </div>
            <div class="benefit-item">
              <mat-icon>verified</mat-icon>
              <h4>Data Integrity</h4>
              <p>Consistent data across all components and modules</p>
            </div>
          </div>
        </section>
      </div>
    </div>
  `,
    styleUrls: ['./dynamic-data-demo.component.scss']
})
export class DynamicDataDemoComponent {
    // Demo form control for the reference selector
    demoControl = new FormControl();
}
